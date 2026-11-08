using Microsoft.AspNetCore.Authorization;
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

                if (document.Components.Schemas is not null)
                {
                    if (document.Components.Schemas.TryGetValue("CreateProduct", out var createRef) &&
                        createRef is OpenApiSchema createSchema &&
                        createSchema.Properties is not null)
                    {
                        createSchema.Required = new HashSet<string>(StringComparer.Ordinal)
                        {
                            "sku", "name", "description", "category", "price", "stockQuantity"
                        };
                        if (createSchema.Properties.TryGetValue("sku", out var skuProp) && skuProp is OpenApiSchema skuSchema)
                        {
                            skuSchema.Type = JsonSchemaType.String;
                            skuSchema.MinLength = 3;
                            skuSchema.MaxLength = 32;
                            skuSchema.Pattern = @"^[A-Z0-9][A-Z0-9\-]{2,31}$";
                        }

                        ConfigureProductBodyProperties(createSchema.Properties);
                    }

                    if (document.Components.Schemas.TryGetValue("UpdateProduct", out var updateRef) &&
                        updateRef is OpenApiSchema updateSchema &&
                        updateSchema.Properties is not null)
                    {
                        updateSchema.Required = new HashSet<string>(StringComparer.Ordinal)
                        {
                            "name", "description", "category", "price", "stockQuantity", "isActive"
                        };
                        ConfigureProductBodyProperties(updateSchema.Properties);
                    }

                    if (document.Components.Schemas.TryGetValue("AdjustProductStock", out var stockRef) &&
                        stockRef is OpenApiSchema stockSchema &&
                        stockSchema.Properties is not null)
                    {
                        stockSchema.Required = new HashSet<string>(StringComparer.Ordinal) { "delta" };
                        if (stockSchema.Properties.TryGetValue("delta", out var deltaProp) && deltaProp is OpenApiSchema deltaSchema)
                        {
                            deltaSchema.Type = JsonSchemaType.Integer;
                            deltaSchema.Minimum = "-1000";
                            deltaSchema.Maximum = "1000";
                        }

                        if (stockSchema.Properties.TryGetValue("reason", out var reasonProp) && reasonProp is OpenApiSchema reasonSchema)
                        {
                            reasonSchema.Type = JsonSchemaType.String | JsonSchemaType.Null;
                            reasonSchema.MaxLength = 200;
                            reasonSchema.Pattern = @"^[^\u0000]*$";
                        }
                    }
                }

                foreach (var (pathKey, pathItem) in document.Paths ?? [])
                {
                    foreach (var (_, operation) in pathItem.Operations ?? [])
                    {
                        if (operation.RequestBody is OpenApiRequestBody reqBody)
                        {
                            reqBody.Required = true;
                        }

                        foreach (var parameter in operation.Parameters ?? [])
                        {
                            if (string.Equals(parameter.Name, "limit", StringComparison.Ordinal) &&
                                parameter.Schema is OpenApiSchema limitSchema)
                            {
                                limitSchema.Type = JsonSchemaType.Integer;
                                limitSchema.Minimum = "1";
                                limitSchema.Maximum = string.Equals(pathKey, "/products", StringComparison.Ordinal) ? "200" : "500";
                            }
                            else if (string.Equals(parameter.Name, "search", StringComparison.Ordinal) &&
                                     parameter.Schema is OpenApiSchema searchSchema)
                            {
                                searchSchema.MaxLength = 100;
                                searchSchema.Pattern = @"^[^\u0000]*$";
                            }
                            else if (string.Equals(parameter.Name, "category", StringComparison.Ordinal) &&
                                     parameter.Schema is OpenApiSchema catSchema)
                            {
                                catSchema.MaxLength = 60;
                                catSchema.Pattern = @"^[^\u0000]*$";
                            }
                        }

                        if (operation.Security is { Count: > 0 })
                        {
                            operation.Security =
                            [
                                new OpenApiSecurityRequirement
                                {
                                    [new OpenApiSecuritySchemeReference("BearerAuth", document)] = []
                                }
                            ];
                        }
                    }
                }

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Rate limit exceeded." });

                if (requiresAuth)
                {
                    operation.Security = [new OpenApiSecurityRequirement()];
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid RS256 Bearer access token." });
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Caller lacks workforce permission for this catalog resource." });
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }

    private static void ConfigureProductBodyProperties(IDictionary<string, IOpenApiSchema> properties)
    {
        ConfigureNonEmptyString(properties, "name", 2, 120);
        ConfigureNonEmptyString(properties, "description", 4, 1000);
        ConfigureNonEmptyString(properties, "category", 2, 60);

        if (properties.TryGetValue("price", out var priceProp) && priceProp is OpenApiSchema priceSchema)
        {
            priceSchema.Type = JsonSchemaType.Number;
            priceSchema.Minimum = "0.01";
            priceSchema.Maximum = "1000000";
        }

        if (properties.TryGetValue("currency", out var currProp) && currProp is OpenApiSchema currSchema)
        {
            currSchema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            currSchema.MinLength = 3;
            currSchema.MaxLength = 3;
            currSchema.Pattern = @"^[A-Z]{3}$";
        }

        if (properties.TryGetValue("stockQuantity", out var stockProp) && stockProp is OpenApiSchema stockSchema)
        {
            stockSchema.Type = JsonSchemaType.Integer;
            stockSchema.Minimum = "0";
            stockSchema.Maximum = "1000000";
        }
    }

    private static void ConfigureNonEmptyString(IDictionary<string, IOpenApiSchema> properties, string name, int minLength, int maxLength)
    {
        if (properties.TryGetValue(name, out var prop) && prop is OpenApiSchema schema)
        {
            schema.Type = JsonSchemaType.String;
            schema.MinLength = minLength;
            schema.MaxLength = maxLength;
            schema.Pattern = @"^[^\u0000]*\S[^\u0000]*\S[^\u0000]*$";
        }
    }
}
