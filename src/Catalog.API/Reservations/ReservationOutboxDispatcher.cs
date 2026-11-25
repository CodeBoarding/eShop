namespace eShop.Catalog.API.Reservations;

public class ReservationOutboxDispatcher(CatalogContext context, IEventBus eventBus, TimeProvider clock)
{
    public Task DispatchPendingAsync(CancellationToken ct = default) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var messages = await context.ReservationOutbox.FromSqlRaw("""
                SELECT * FROM "ReservationOutbox"
                WHERE "PublishedAt" IS NULL ORDER BY "CreatedAt", "Id"
                LIMIT 100 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            foreach (var message in messages)
            {
                await eventBus.PublishAsync(message.ToEvent());
                message.PublishedAt = clock.GetUtcNow();
            }
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            // A crash after publish can replay a message. Consumers must be idempotent.
        });
}
