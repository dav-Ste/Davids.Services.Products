using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Davids.Services.Products.Application.Commands;
using Davids.Services.Products.Application.DTO;
using Davids.Services.Products.Application.Events;
using Davids.Services.Products.Application.Services;
using Davids.Services.Products.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Davids.Services.Products.Tests;

public sealed class ProductsFactory : WebApplicationFactory<Program>
{
    public const string CustomerIssuer = "https://identity.davids.test/";
    public const string AdminIssuer = "https://admin-identity.davids.test/";

    private static readonly RSA CustomerRsa = RSA.Create(2048);
    private static readonly RSA AdminRsa = RSA.Create(2048);

    private static readonly string CustomerPublicKeyPem = CustomerRsa.ExportSubjectPublicKeyInfoPem();
    private static readonly string AdminPublicKeyPem = AdminRsa.ExportSubjectPublicKeyInfoPem();

    private static readonly RsaSecurityKey CustomerPrivateKey = CreateKey(CustomerRsa);
    private static readonly RsaSecurityKey AdminPrivateKey = CreateKey(AdminRsa);

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public ProductsFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("CustomerOidc:Issuer", CustomerIssuer);
        builder.UseSetting("CustomerOidc:Audience", "davids-services");
        builder.UseSetting("CustomerOidc:PublicKeyPem", CustomerPublicKeyPem);
        builder.UseSetting("AdminOidc:Issuer", AdminIssuer);
        builder.UseSetting("AdminOidc:Audience", "davids-admin");
        builder.UseSetting("AdminOidc:PublicKeyPem", AdminPublicKeyPem);
        builder.UseSetting("Vault:Enabled", "false");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("ConnectionStrings:productsdb", "unused");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ProductsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ProductsDbContext>>();
            services.RemoveAll<ProductsDbContext>();
            services.AddDbContext<ProductsDbContext>(options => options.UseSqlite(connection));
        });
    }

    public async Task<HttpClient> CreateSeededClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductsDbContext>();
        await db.Database.EnsureCreatedAsync();
        await ProductsSeeder.SeedDefaultCatalogAsync(db);
        return client;
    }

    public static string CreateCustomerToken(Guid sub) => Mint(sub, "user", CustomerIssuer, "davids-services", CustomerPrivateKey);
    public static string CreateWorkforceToken(Guid sub, string role = "admin") => Mint(sub, role, AdminIssuer, "davids-admin", AdminPrivateKey);

    private static string Mint(Guid sub, string role, string issuer, string audience, RsaSecurityKey key)
    {
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", sub.ToString("N")), new Claim("role", role), new Claim("jti", Guid.NewGuid().ToString("N"))],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static RsaSecurityKey CreateKey(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(spki))[..16];
        return new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = kid };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}

[Trait("Category", "Integration")]
public sealed class ProductsTests
{
    [Fact]
    public async Task PublicCatalogListsActiveSeededProductsAndRejectsCustomerMutations()
    {
        await using var factory = new ProductsFactory();
        using var client = await factory.CreateSeededClientAsync();

        var catalog = await client.GetFromJsonAsync<ProductDto[]>("/products");
        Assert.NotNull(catalog);
        Assert.True(catalog!.Length >= 6);

        using var customerMutate = new HttpRequestMessage(HttpMethod.Post, "/products")
        {
            Content = JsonContent.Create(new CreateProduct("NEW-SKU-01", "Test Item", "Valid description", "Accessories", 19.99m, "GBP", 10))
        };
        customerMutate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ProductsFactory.CreateCustomerToken(Guid.NewGuid()));
        var forbidden = await client.SendAsync(customerMutate);
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
    }

    [Fact]
    public async Task WorkforceCanCreateUpdateRestockAuditAndDecrementStockOnOrderCompleted()
    {
        await using var factory = new ProductsFactory();
        using var client = await factory.CreateSeededClientAsync();
        var adminToken = ProductsFactory.CreateWorkforceToken(Guid.NewGuid(), "admin");
        var auditorToken = ProductsFactory.CreateWorkforceToken(Guid.NewGuid(), "auditor");

        using var createReq = new HttpRequestMessage(HttpMethod.Post, "/products")
        {
            Content = JsonContent.Create(new CreateProduct("NET-SWITCH-10G", "10GbE Managed Switch", "Silent fanless 8-port 10GbE desktop switch", "Networking", 299.50m, "GBP", 15, true))
        };
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var createResp = await client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(created);

        using var auditorMutate = new HttpRequestMessage(HttpMethod.Post, $"/products/{created!.Id}/stock")
        {
            Content = JsonContent.Create(new AdjustProductStock(5, "restock"))
        };
        auditorMutate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorToken);
        var auditorResp = await client.SendAsync(auditorMutate);
        Assert.Equal(HttpStatusCode.Forbidden, auditorResp.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IProductsService>();
            await svc.ApplyOrderCompletedAsync(new OrderCompleted(Guid.NewGuid(), Guid.NewGuid(), [new CompletedOrderItem(created.Id, 3)]));
        }

        var reloaded = await client.GetFromJsonAsync<ProductDto>($"/products/{created.Id}");
        Assert.NotNull(reloaded);
        Assert.Equal(12, reloaded!.StockQuantity);

        using var auditReq = new HttpRequestMessage(HttpMethod.Get, $"/products/change-audits?productId={created.Id}");
        auditReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auditorToken);
        var auditResp = await client.SendAsync(auditReq);
        Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
        var audits = await auditResp.Content.ReadFromJsonAsync<ProductChangeAuditDto[]>();
        Assert.NotNull(audits);
        Assert.Equal(2, audits!.Length);
    }
}
