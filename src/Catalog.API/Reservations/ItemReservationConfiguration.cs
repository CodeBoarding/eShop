namespace eShop.Catalog.API.Reservations;

public static class ItemReservationConfiguration
{
    public static void ConfigureItemReservations(this ModelBuilder builder)
    {
        builder.Entity<ItemReservation>(entity =>
        {
            entity.ToTable("ItemReservations");
            entity.HasKey(x => x.OrderId);
            entity.Property(x => x.OrderId).ValueGeneratedNever();
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasIndex(x => new { x.State, x.ExpiresAt });
            entity.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId);
        });
        builder.Entity<ReservedItem>(entity =>
        {
            entity.ToTable("ReservedItems");
            entity.HasKey(x => new { x.OrderId, x.ProductId });
            entity.ToTable(t => t.HasCheckConstraint("CK_ReservedItems_Units", "\"Units\" > 0"));
            // Retain the item identity until release/completion; deleting it must not lose held units.
            entity.HasOne<CatalogItem>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ReservationOutboxMessage>(entity =>
        {
            entity.ToTable("ReservationOutbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(150);
            entity.HasIndex(x => new { x.PublishedAt, x.CreatedAt });
        });
    }
}
