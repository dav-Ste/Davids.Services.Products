using Davids.Services.Products.Core.Entities;

namespace Davids.Services.Products.Core.Repositories;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(string normalizedSku, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchAsync(string? search, string? category, bool includeInactive, int limit, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
    Task AddAuditAsync(ProductChangeAuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<ProductChangeAuditEntry>> GetAuditsAsync(Guid? productId, int limit, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
