namespace Davids.Services.Products.Application.DTO;

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    string Category,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProductChangeAuditDto(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string Action,
    string ActorSub,
    string? ActorEmail,
    string ActorRole,
    string ActorAuthority,
    string ChangedFieldsJson,
    string? CorrelationId,
    DateTime OccurredAtUtc);

public sealed record ProductActorMetadata(
    string ActorSub,
    string? ActorEmail,
    string ActorRole,
    string ActorAuthority,
    string? CorrelationId);
