namespace Davids.Services.Products.Core.Exceptions;

public abstract class DomainException(string message) : Exception(message)
{
    public abstract string Code { get; }
}

public sealed class ProductNotFoundException(Guid id)
    : DomainException($"Product with id '{id}' was not found.")
{
    public override string Code => "product_not_found";
}

public sealed class DuplicateProductSkuException(string sku)
    : DomainException($"A product with SKU '{sku}' already exists.")
{
    public override string Code => "duplicate_product_sku";
}

public sealed class InvalidProductException(string message)
    : DomainException(message)
{
    public override string Code => "invalid_product";
}

public sealed class InsufficientProductStockException(Guid productId, int requested, int available)
    : DomainException($"Product '{productId}' has insufficient stock (requested {requested}, available {available}).")
{
    public override string Code => "insufficient_product_stock";
}
