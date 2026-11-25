using Microsoft.EntityFrameworkCore.Design;

namespace eShop.Catalog.API.Infrastructure;

public class CatalogContextDesignFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<CatalogContext>()
            .UseNpgsql("Host=localhost;Database=catalogdb", options => options.UseVector()).Options,
        new ConfigurationBuilder().Build());
}
