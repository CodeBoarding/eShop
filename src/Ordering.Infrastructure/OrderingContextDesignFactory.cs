namespace eShop.Ordering.Infrastructure;

public class OrderingContextDesignFactory : IDesignTimeDbContextFactory<OrderingContext>
{
    public OrderingContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<OrderingContext>()
            .UseNpgsql("Host=localhost;Database=orderingdb").Options);
}
