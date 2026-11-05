using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Davids.Services.Products.Api;

public static class ProductsOpenApiExtensions
{
    public static IServiceCollection AddProductsOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Davids Products API";
                document.Info.Version = "1.0.0";
                document.Info.Description = "Product catalog, inventory stock management, and workforce catalog change-audit endpoints.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "RS256 OIDC access token (AdminBearer aud: davids-admin for workforce catalog management)."
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
