using System.Text.Json;
using Davids.Services.Products.Application.Commands;
using Davids.Services.Products.Application.DTO;
using Davids.Services.Products.Application.Events;
using Davids.Services.Products.Core.Entities;
using Davids.Services.Products.Core.Exceptions;
using Davids.Services.Products.Core.Repositories;

namespace Davids.Services.Products.Application.Services;

public sealed class ProductsService(
    IProductRepository repository,
    IOutboxWriter outbox) : IProductsService
{
    public async Task<ProductDto?> GetPublicAsync(Guid id, CancellationToken ct = default)
    {
        var product = await repository.GetAsync(id, ct);
        return product is { IsActive: true } ? Map(product) : null;
    }

    public async Task<ProductDto?> GetAdminAsync(Guid id, CancellationToken ct = default)
    {
        var product = await repository.GetAsync(id, ct);
        return product is null ? null : Map(product);
    }

    public async Task<IReadOnlyList<ProductDto>> SearchPublicAsync(
        string? search,
        string? category,
        int limit = 100,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 200);
        var items = await repository.SearchAsync(search, category, includeInactive: false, clamped, ct);
        return items.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<ProductDto>> SearchAdminAsync(
        string? search,
        string? category,
        int limit = 200,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 500);
        var items = await repository.SearchAsync(search, category, includeInactive: true, clamped, ct);
        return items.Select(Map).ToArray();
    }

    public async Task<ProductDto> CreateAsync(
        CreateProduct command,
        ProductActorMetadata actor,
        CancellationToken ct = default)
    {
        var normalizedSku = Product.NormalizeAndValidateSku(command.Sku);
        var existing = await repository.GetBySkuAsync(normalizedSku, ct);
        if (existing is not null)
        {
            throw new DuplicateProductSkuException(normalizedSku);
        }

        var now = DateTime.UtcNow;
        var product = new Product(
            Guid.NewGuid(),
            normalizedSku,
            command.Name,
            command.Description,
            command.Category,
            command.Price,
            command.Currency ?? "GBP",
            command.StockQuantity,
            command.IsActive,
            now);

        await repository.AddAsync(product, ct);
        await repository.AddAuditAsync(new ProductChangeAuditEntry
        {
            ProductId = product.Id,
            ProductSku = product.Sku,
            Action = "product_created",
            ActorSub = actor.ActorSub,
            ActorEmail = actor.ActorEmail,
            ActorRole = actor.ActorRole,
            ActorAuthority = actor.ActorAuthority,
            CorrelationId = actor.CorrelationId,
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                product.Sku,
                product.Name,
                product.Category,
                product.Price,
                product.Currency,
                product.StockQuantity,
                product.IsActive
            }),
            OccurredAtUtc = now
        }, ct);

        EnqueueUpserted(product);
        await repository.SaveChangesAsync(ct);
        return Map(product);
    }

    public async Task<ProductDto> UpdateAsync(
        Guid id,
        UpdateProduct command,
        ProductActorMetadata actor,
        CancellationToken ct = default)
    {
        var product = await repository.GetAsync(id, ct)
            ?? throw new ProductNotFoundException(id);

        var now = DateTime.UtcNow;
        var beforePrice = product.Price;
        var beforeStock = product.StockQuantity;
        var beforeActive = product.IsActive;

        product.Update(
            command.Name,
            command.Description,
            command.Category,
            command.Price,
            command.Currency ?? product.Currency,
            command.StockQuantity,
            command.IsActive,
            now);

        await repository.AddAuditAsync(new ProductChangeAuditEntry
        {
            ProductId = product.Id,
            ProductSku = product.Sku,
            Action = "product_updated",
            ActorSub = actor.ActorSub,
            ActorEmail = actor.ActorEmail,
            ActorRole = actor.ActorRole,
            ActorAuthority = actor.ActorAuthority,
            CorrelationId = actor.CorrelationId,
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                product.Name,
                product.Category,
                BeforePrice = beforePrice,
                AfterPrice = product.Price,
                BeforeStock = beforeStock,
                AfterStock = product.StockQuantity,
                BeforeActive = beforeActive,
                AfterActive = product.IsActive
            }),
            OccurredAtUtc = now
        }, ct);

        EnqueueUpserted(product);
        await repository.SaveChangesAsync(ct);
        return Map(product);
    }

    public async Task<ProductDto> AdjustStockAsync(
        Guid id,
        AdjustProductStock command,
        ProductActorMetadata actor,
        CancellationToken ct = default)
    {
        if (command.Reason is { Length: > 200 } || (command.Reason is not null && command.Reason.Contains('\0')))
        {
            throw new InvalidProductException("Stock adjustment reason must be at most 200 characters.");
        }

        var product = await repository.GetAsync(id, ct)
            ?? throw new ProductNotFoundException(id);

        var now = DateTime.UtcNow;
        var before = product.StockQuantity;
        product.AdjustStock(command.Delta, now);

        await repository.AddAuditAsync(new ProductChangeAuditEntry
        {
            ProductId = product.Id,
            ProductSku = product.Sku,
            Action = "product_stock_adjusted",
            ActorSub = actor.ActorSub,
            ActorEmail = actor.ActorEmail,
            ActorRole = actor.ActorRole,
            ActorAuthority = actor.ActorAuthority,
            CorrelationId = actor.CorrelationId,
            ChangedFieldsJson = JsonSerializer.Serialize(new
            {
                Delta = command.Delta,
                BeforeStock = before,
                AfterStock = product.StockQuantity,
                Reason = command.Reason ?? "manual_adjustment"
            }),
            OccurredAtUtc = now
        }, ct);

        EnqueueUpserted(product);
        await repository.SaveChangesAsync(ct);
        return Map(product);
    }

    public async Task<IReadOnlyList<ProductChangeAuditDto>> GetAuditsAsync(
        Guid? productId,
        int limit = 100,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 500);
        var entries = await repository.GetAuditsAsync(productId, clamped, ct);
        return entries.Select(e => new ProductChangeAuditDto(
            e.Id,
            e.ProductId,
            e.ProductSku,
            e.Action,
            e.ActorSub,
            e.ActorEmail,
            e.ActorRole,
            e.ActorAuthority,
            e.ChangedFieldsJson,
            e.CorrelationId,
            DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc))).ToArray();
    }

    public async Task ApplyOrderCompletedAsync(OrderCompleted evt, CancellationToken ct = default)
    {
        if (evt.Items is null || evt.Items.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var item in evt.Items)
        {
            var product = await repository.GetAsync(item.ProductId, ct);
            if (product is null || item.Quantity <= 0)
            {
                continue;
            }

            var before = product.StockQuantity;
            product.DecrementForCompletedOrder(item.Quantity, now);
            await repository.AddAuditAsync(new ProductChangeAuditEntry
            {
                ProductId = product.Id,
                ProductSku = product.Sku,
                Action = "order_stock_decremented",
                ActorSub = evt.CustomerId.ToString("N"),
                ActorRole = "system",
                ActorAuthority = "orders-sync",
                ChangedFieldsJson = JsonSerializer.Serialize(new
                {
                    evt.OrderId,
                    item.Quantity,
                    BeforeStock = before,
                    AfterStock = product.StockQuantity
                }),
                OccurredAtUtc = now
            }, ct);
            EnqueueUpserted(product);
        }

        await repository.SaveChangesAsync(ct);
    }

    private void EnqueueUpserted(Product p)
        => outbox.Enqueue("product_upserted", new ProductUpserted(
            p.Id, p.Sku, p.Name, p.Category, p.Price, p.Currency, p.StockQuantity, p.IsActive));

    private static ProductDto Map(Product p)
        => new(
            p.Id,
            p.Sku,
            p.Name,
            p.Description,
            p.Category,
            p.Price,
            p.Currency,
            p.StockQuantity,
            p.IsActive,
            DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(p.UpdatedAt, DateTimeKind.Utc));
}
