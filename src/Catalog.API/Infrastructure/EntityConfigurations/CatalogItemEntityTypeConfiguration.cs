namespace eShop.Catalog.API.Infrastructure.EntityConfigurations;

class CatalogItemEntityTypeConfiguration
    : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("Catalog");

        builder.Property(ci => ci.Name)
            .HasMaxLength(50);

        builder.Property(ci => ci.Embedding)
            .HasColumnType("vector(384)");

        builder.HasOne(ci => ci.CatalogBrand)
            .WithMany();

        builder.HasOne(ci => ci.CatalogType)
            .WithMany();

        builder.HasIndex(ci => ci.Name);
        // Existing catalog editing must not overwrite a quantity read before a reservation.
        builder.Property(ci => ci.AvailableStock).IsConcurrencyToken();
    }
}
