using System.Text;
using System.Text.Json;
using Davids.Services.Products.Application.Events;
using Davids.Services.Products.Application.Services;
using Davids.Services.Products.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Davids.Services.Products.Infrastructure.Messaging;

public sealed class ProductsMessageWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ProductsMessageWorker> logger) : BackgroundService
{
    private const string OrderCompletedQueue = "products-service/orders.order_completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Products RabbitMQ worker disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Missing RabbitMQ messaging connection string.");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var publisher = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await using var consumerChannel = await connection.CreateChannelAsync(cancellationToken: ct);

        await publisher.ExchangeDeclareAsync("products", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.ExchangeDeclareAsync("orders", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.ExchangeDeclareAsync("products.dead", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.QueueDeclareAsync("products-service.dead", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await consumerChannel.QueueBindAsync("products-service.dead", "products.dead", "#", cancellationToken: ct);

        var dlxArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "products.dead" };
        await consumerChannel.QueueDeclareAsync(OrderCompletedQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs, cancellationToken: ct);
        await consumerChannel.QueueBindAsync(OrderCompletedQueue, "orders", "order_completed", cancellationToken: ct);
        await consumerChannel.BasicQosAsync(0, 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(consumerChannel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            var messageId = args.BasicProperties.MessageId;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                messageId = $"{args.RoutingKey}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(args.Body.Span))[..24]}";
            }

            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
                if (!await db.Inbox.AnyAsync(x => x.Id == messageId, ct))
                {
                    var evt = JsonSerializer.Deserialize<OrderCompleted>(args.Body.Span, JsonOptions);
                    if (evt is not null)
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IProductsService>();
                        await service.ApplyOrderCompletedAsync(evt, ct);
                    }

                    db.Inbox.Add(new InboxMessage { Id = messageId, ReceivedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                }

                await consumerChannel.BasicAckAsync(args.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing {RoutingKey} message {MessageId} in Products.", args.RoutingKey, messageId);
                await consumerChannel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, ct);
            }
        };

        await consumerChannel.BasicConsumeAsync(OrderCompletedQueue, autoAck: false, consumer, ct);

        while (!ct.IsCancellationRequested)
        {
            await PublishPendingOutboxAsync(publisher, ct);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task PublishPendingOutboxAsync(IChannel publisher, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
        var pending = await db.Outbox
            .Where(x => x.PublishedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var msg in pending)
        {
            var props = new BasicProperties
            {
                MessageId = msg.Id,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = msg.Type
            };
            await publisher.BasicPublishAsync(
                exchange: "products",
                routingKey: msg.Type,
                mandatory: false,
                basicProperties: props,
                body: Encoding.UTF8.GetBytes(msg.Payload),
                cancellationToken: ct);
            msg.PublishedAt = DateTime.UtcNow;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
