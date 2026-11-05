using Davids.Services.Products.Application.Services;
using Davids.Services.Products.Core.Repositories;
using Davids.Services.Products.Infrastructure.Auth;
using Davids.Services.Products.Infrastructure.Messaging;
using Davids.Services.Products.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Davids.Services.Products.Infrastructure;

public static class ProductsInfrastructureExtensions
{
    public const string CustomerScheme = "CustomerBearer";
    public const string AdminScheme = "AdminBearer";

    public static IServiceCollection AddProductsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var customerOidc = configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-services" };
        customerOidc.Audience ??= "davids-services";
        customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");
        services.AddSingleton(customerOidc);

        var adminOidc = configuration.GetSection("AdminOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-admin" };
        adminOidc.Audience ??= "davids-admin";
        adminOidc.ValidateAndResolveStaticPublicKey("AdminOidc");

        var connectionString = configuration.GetConnectionString("productsdb")
            ?? throw new InvalidOperationException("Missing productsdb connection.");
        services.AddDbContext<ProductsDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IProductsService, ProductsService>();

        if (configuration.GetValue("Messaging:Enabled", true))
        {
            services.AddHostedService<ProductsMessageWorker>();
        }

        services.AddHealthChecks().AddCheck<ProductsDatabaseHealthCheck>("products-database");
        return services;
    }
}
