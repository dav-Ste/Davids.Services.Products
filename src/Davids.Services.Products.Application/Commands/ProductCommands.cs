using System.Text.Json.Serialization;

namespace Davids.Services.Products.Application.Commands;

public sealed record CreateProduct(
    [property: JsonRequired] string Sku,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Description,
    [property: JsonRequired] string Category,
    [property: JsonRequired] decimal Price,
    string? Currency,
    [property: JsonRequired] int StockQuantity,
    bool IsActive = true);

public sealed record UpdateProduct(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Description,
    [property: JsonRequired] string Category,
    [property: JsonRequired] decimal Price,
    string? Currency,
    [property: JsonRequired] int StockQuantity,
    [property: JsonRequired] bool IsActive);

public sealed record AdjustProductStock(
    [property: JsonRequired] int Delta,
    string? Reason = null);
