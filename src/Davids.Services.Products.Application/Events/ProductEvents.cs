namespace Davids.Services.Products.Application.Events;

public sealed record ProductUpserted(
    Guid ProductId,
    string Sku,
    string Name,
    string Category,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool IsActive);

public sealed record CompletedOrderItem(Guid ProductId, int Quantity);

public sealed record OrderCompleted(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<CompletedOrderItem>? Items = null);
