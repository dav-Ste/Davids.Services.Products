namespace Davids.Services.Products.Core.Entities;

public sealed class ProductChangeAuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductId { get; init; }
    public string ProductSku { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ActorSub { get; init; } = string.Empty;
    public string? ActorEmail { get; init; }
    public string ActorRole { get; init; } = string.Empty;
    public string ActorAuthority { get; init; } = string.Empty;
    public string ChangedFieldsJson { get; init; } = "{}";
    public string? CorrelationId { get; init; }
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
