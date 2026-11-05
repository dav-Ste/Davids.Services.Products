using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Davids.Services.Products.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ProductsDbContext>
{
    public ProductsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlServer("Server=localhost;Database=productsdb;TrustServerCertificate=True")
            .Options;
        return new ProductsDbContext(options);
    }
}
