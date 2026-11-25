namespace eShop.Catalog.API.Infrastructure;

/// <remarks>
/// Add migrations using the following command inside the 'Catalog.API' project directory:
///
/// dotnet ef migrations add --context CatalogContext [migration-name]
/// </remarks>
public class CatalogContext : DbContext
{
    public CatalogContext(DbContextOptions<CatalogContext> options, IConfiguration configuration) : base(options)
    {
    }

    public DbSet<CatalogItem> CatalogItems { get; set; } = null!;
    public DbSet<CatalogBrand> CatalogBrands { get; set; } = null!;
    public DbSet<CatalogType> CatalogTypes { get; set; } = null!;

    public DbSet<eShop.Catalog.API.Reservations.ItemReservation> ItemReservations => Set<eShop.Catalog.API.Reservations.ItemReservation>();
    public DbSet<eShop.Catalog.API.Reservations.ReservationOutboxMessage> ReservationOutbox => Set<eShop.Catalog.API.Reservations.ReservationOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        eShop.Catalog.API.Reservations.ItemReservationConfiguration.ConfigureItemReservations(builder);
        builder.HasPostgresExtension("vector");
        builder.ApplyConfiguration(new CatalogBrandEntityTypeConfiguration());
        builder.ApplyConfiguration(new CatalogTypeEntityTypeConfiguration());
        builder.ApplyConfiguration(new CatalogItemEntityTypeConfiguration());

        // Add the outbox table to this context
        builder.UseIntegrationEventLogs();
    }
}
