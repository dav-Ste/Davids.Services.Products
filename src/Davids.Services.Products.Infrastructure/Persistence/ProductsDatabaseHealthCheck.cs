using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Davids.Services.Products.Infrastructure.Persistence;

public sealed class ProductsDatabaseHealthCheck(ProductsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to productsdb.");
    }
}
