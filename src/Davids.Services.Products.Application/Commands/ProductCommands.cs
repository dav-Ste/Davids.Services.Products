namespace Davids.Services.Products.Application.Commands;

public sealed record CreateProduct(
    string Sku,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string? Currency,
    int StockQuantity,
    bool IsActive = true);

public sealed record UpdateProduct(
    string Name,
    string Description,
    string Category,
    decimal Price,
    string? Currency,
    int StockQuantity,
    bool IsActive);

public sealed record AdjustProductStock(int Delta, string? Reason = null);
