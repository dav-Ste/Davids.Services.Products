using Davids.Services.Products.Application.Commands;
using Davids.Services.Products.Application.DTO;
using Davids.Services.Products.Application.Events;

namespace Davids.Services.Products.Application.Services;

public interface IProductsService
{
    Task<ProductDto?> GetPublicAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto?> GetAdminAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> SearchPublicAsync(string? search, string? category, int limit = 100, CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> SearchAdminAsync(string? search, string? category, int limit = 200, CancellationToken ct = default);
    Task<ProductDto> CreateAsync(CreateProduct command, ProductActorMetadata actor, CancellationToken ct = default);
    Task<ProductDto> UpdateAsync(Guid id, UpdateProduct command, ProductActorMetadata actor, CancellationToken ct = default);
    Task<ProductDto> AdjustStockAsync(Guid id, AdjustProductStock command, ProductActorMetadata actor, CancellationToken ct = default);
    Task<IReadOnlyList<ProductChangeAuditDto>> GetAuditsAsync(Guid? productId, int limit = 100, CancellationToken ct = default);
    Task ApplyOrderCompletedAsync(OrderCompleted evt, CancellationToken ct = default);
}
