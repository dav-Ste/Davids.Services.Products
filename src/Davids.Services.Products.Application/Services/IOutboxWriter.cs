namespace Davids.Services.Products.Application.Services;

public interface IOutboxWriter
{
    void Enqueue<T>(string type, T message);
}
