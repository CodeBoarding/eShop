namespace eShop.Ordering.Infrastructure.Repositories;

public class OrderRepository
    : IOrderRepository
{
    private readonly OrderingContext _context;

    public IUnitOfWork UnitOfWork => _context;

    public OrderRepository(OrderingContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Order Add(Order order)
    {
        return _context.Orders.Add(order).Entity;

    }

    public async Task<Order> GetAsync(int orderId)
    {
        if (_context.HasActiveTransaction)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(7317, {orderId})");
        }
        var order = await _context.Orders.FindAsync(orderId);

        if (order != null)
        {
            await _context.Entry(order)
                .Collection(i => i.OrderItems).LoadAsync();
        }

        return order;
    }

    public void Update(Order order)
    {
        _context.Entry(order).State = EntityState.Modified;
    }
}
