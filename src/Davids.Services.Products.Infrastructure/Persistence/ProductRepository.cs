using Davids.Services.Products.Core.Entities;
using Davids.Services.Products.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Products.Infrastructure.Persistence;

public sealed class ProductRepository(ProductsDbContext db) : IProductRepository
{
    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Product?> GetBySkuAsync(string normalizedSku, CancellationToken ct = default)
        => db.Products.FirstOrDefaultAsync(x => x.Sku == normalizedSku, ct);

    public async Task<IReadOnlyList<Product>> SearchAsync(
        string? search,
        string? category,
        bool includeInactive,
        int limit,
        CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            query = query.Where(x => x.Category == cat);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Name.ToLower().Contains(term) ||
                x.Sku.ToLower().Contains(term) ||
                x.Description.ToLower().Contains(term) ||
                x.Category.ToLower().Contains(term));
        }

        return await query
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task AddAsync(Product product, CancellationToken ct = default)
        => db.Products.AddAsync(product, ct).AsTask();

    public Task AddAuditAsync(ProductChangeAuditEntry entry, CancellationToken ct = default)
        => db.ProductChangeAuditLogs.AddAsync(entry, ct).AsTask();

    public async Task<IReadOnlyList<ProductChangeAuditEntry>> GetAuditsAsync(
        Guid? productId,
        int limit,
        CancellationToken ct = default)
    {
        var query = db.ProductChangeAuditLogs.AsNoTracking().AsQueryable();
        if (productId.HasValue)
        {
            query = query.Where(x => x.ProductId == productId.Value);
        }

        return await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
