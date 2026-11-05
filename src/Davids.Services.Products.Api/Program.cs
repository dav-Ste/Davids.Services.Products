using System.Security.Claims;
using Davids.ServiceDefaults;
using Davids.Services.Products.Api;
using Davids.Services.Products.Api.Exceptions;
using Davids.Services.Products.Application.Commands;
using Davids.Services.Products.Application.DTO;
using Davids.Services.Products.Application.Services;
using Davids.Services.Products.Infrastructure;
using Davids.Services.Products.Infrastructure.Auth;
using Davids.Services.Products.Infrastructure.Persistence;
using Davids.Services.Products.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.LoadVaultAsync();
builder.AddServiceDefaults("davids-products");
builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);
builder.Services.AddProductsInfrastructure(builder.Configuration);

var customerOidc = builder.Configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-services" };
customerOidc.Audience ??= "davids-services";
var customerStaticKey = customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");

var adminOidc = builder.Configuration.GetSection("AdminOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-admin" };
adminOidc.Audience ??= "davids-admin";
var adminStaticKey = adminOidc.ValidateAndResolveStaticPublicKey("AdminOidc");

var requireHttpsMetadata =
    builder.Environment.IsProduction() ||
    !(builder.Environment.IsDevelopment() ||
      builder.Environment.IsEnvironment("Testing") ||
      builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment"));

builder.Services.AddAuthentication(ProductsInfrastructureExtensions.CustomerScheme)
    .AddJwtBearer(ProductsInfrastructureExtensions.CustomerScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(customerOidc.MetadataAddress))
        {
            options.MetadataAddress = customerOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(customerOidc.EffectiveIssuer),
            ValidIssuer = customerOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = customerOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = customerStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!string.Equals(role, DavidsRoles.Customer, StringComparison.Ordinal))
                {
                    context.Fail("CustomerBearer accepts customer ('user') tokens only.");
                }

                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer(ProductsInfrastructureExtensions.AdminScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(adminOidc.MetadataAddress))
        {
            options.MetadataAddress = adminOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(adminOidc.EffectiveIssuer),
            ValidIssuer = adminOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = adminOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = adminStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!DavidsRoles.CanReadWorkforce(role))
                {
                    context.Fail("AdminBearer accepts workforce roles (admin, support, auditor) only.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("WorkforceRead", policy =>
    {
        policy.AddAuthenticationSchemes(ProductsInfrastructureExtensions.AdminScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Admin, DavidsRoles.Support, DavidsRoles.Auditor);
    })
    .AddPolicy("WorkforceMutate", policy =>
    {
        policy.AddAuthenticationSchemes(ProductsInfrastructureExtensions.AdminScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Admin, DavidsRoles.Support);
    });

builder.Services.AddExceptionHandler<ProductApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddProductsOpenApi();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Automatic migration is development-only.");
    }

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
    await db.Database.MigrateAsync();
    var seedDefaults = builder.Configuration.GetValue<bool?>("SeedProducts:Enabled")
        ?? builder.Configuration.GetValue<bool?>("Products:SeedDefaults")
        ?? true;
    if (seedDefaults)
    {
        await ProductsSeeder.SeedDefaultCatalogAsync(db);
    }
}

app.UseTrustedForwardedHeaders();
app.UseExceptionHandler();
if (!(app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment")))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "docs";
        options.SwaggerEndpoint("../openapi/v1.json", "Davids Products v1");
    });
}

static ProductActorMetadata ResolveActor(ClaimsPrincipal principal, HttpContext httpContext)
{
    var actor = DavidsActorContext.FromPrincipal(principal, httpContext);
    return new ProductActorMetadata(
        actor.ActorSub,
        actor.ActorEmail,
        actor.ActorRole,
        actor.ActorAuthority,
        actor.CorrelationId);
}

app.MapGet("/", () => Results.Text("Davids Products Service"))
    .ExcludeFromDescription()
    .AllowAnonymous();

app.MapGet("/products", async (string? search, string? category, int? limit, IProductsService service, CancellationToken ct) =>
    {
        var items = await service.SearchPublicAsync(search, category, limit ?? 100, ct);
        return Results.Ok(items);
    })
    .AllowAnonymous();

app.MapGet("/products/{productId:guid}", async (Guid productId, IProductsService service, CancellationToken ct) =>
    {
        var item = await service.GetPublicAsync(productId, ct);
        return item is null ? Results.NotFound() : Results.Ok(item);
    })
    .AllowAnonymous();

app.MapGet("/products/admin", async (string? search, string? category, int? limit, IProductsService service, CancellationToken ct) =>
    {
        var items = await service.SearchAdminAsync(search, category, limit ?? 200, ct);
        return Results.Ok(items);
    })
    .RequireAuthorization("WorkforceRead");

app.MapGet("/products/admin/{productId:guid}", async (Guid productId, IProductsService service, CancellationToken ct) =>
    {
        var item = await service.GetAdminAsync(productId, ct);
        return item is null ? Results.NotFound() : Results.Ok(item);
    })
    .RequireAuthorization("WorkforceRead");

app.MapPost("/products", async (CreateProduct command, ClaimsPrincipal user, HttpContext ctx, IProductsService service, CancellationToken ct) =>
    {
        var created = await service.CreateAsync(command, ResolveActor(user, ctx), ct);
        return Results.Created($"/products/{created.Id}", created);
    })
    .RequireAuthorization("WorkforceMutate");

app.MapPut("/products/{productId:guid}", async (Guid productId, UpdateProduct command, ClaimsPrincipal user, HttpContext ctx, IProductsService service, CancellationToken ct) =>
    {
        var updated = await service.UpdateAsync(productId, command, ResolveActor(user, ctx), ct);
        return Results.Ok(updated);
    })
    .RequireAuthorization("WorkforceMutate");

app.MapPost("/products/{productId:guid}/stock", async (Guid productId, AdjustProductStock command, ClaimsPrincipal user, HttpContext ctx, IProductsService service, CancellationToken ct) =>
    {
        var updated = await service.AdjustStockAsync(productId, command, ResolveActor(user, ctx), ct);
        return Results.Ok(updated);
    })
    .RequireAuthorization("WorkforceMutate");

app.MapGet("/products/change-audits", async (Guid? productId, int? limit, IProductsService service, CancellationToken ct) =>
    {
        var audits = await service.GetAuditsAsync(productId, limit ?? 100, ct);
        return Results.Ok(audits);
    })
    .RequireAuthorization("WorkforceRead");

app.Run();

public partial class Program;
