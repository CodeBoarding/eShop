using eShop.ItemReservation.Contracts;

namespace eShop.Catalog.API.Reservations;

/// <summary>
/// Catalog is the single authority for reservable quantities and reservation outcomes.
/// All commands lock the order, then item rows in product-id order, within one transaction.
/// </summary>
public class ItemReservationService(
    CatalogContext context, TimeProvider clock, IOptions<ItemReservationOptions> options)
{
    public Task ReserveAsync(int orderId, IEnumerable<ItemReservationRequest> requested, CancellationToken ct = default)
    {
        var input = requested.Take(101).ToArray();
        return InTransactionAsync(orderId, async () =>
        {
            if (await FindAsync(orderId, ct) is not null)
                return; // Order IDs are single-use, including rejected/released tombstones.

            var now = clock.GetUtcNow();
            var reservation = new ItemReservation { OrderId = orderId, CreatedAt = now };
            context.ItemReservations.Add(reservation);
            if (input.Length is 0 or > 100 || input.Any(x => x.ProductId <= 0 || x.Units <= 0))
            {
                Reject(reservation, "An item reservation requires 1–100 valid item lines.");
                return;
            }

            var quantities = input.GroupBy(x => x.ProductId)
                .OrderBy(x => x.Key).Select(x => new { ProductId = x.Key, Units = x.Sum(i => (long)i.Units) }).ToArray();
            var items = new List<(CatalogItem Item, int Units)>();
            foreach (var line in quantities)
            {
                var item = await LockItemAsync(line.ProductId, ct);
                if (item is null || line.Units > item.AvailableStock)
                {
                    Reject(reservation, $"Item {line.ProductId} is unavailable in the requested quantity.");
                    return; // No quantities have been changed yet: the basket is all-or-nothing.
                }
                items.Add((item, (int)line.Units));
            }

            reservation.State = ItemReservationState.Active;
            reservation.ExpiresAt = clock.GetUtcNow().AddSeconds(options.Value.DurationSeconds);
            foreach (var (item, units) in items)
            {
                item.AvailableStock -= units;
                reservation.Items.Add(new ReservedItem { OrderId = orderId, ProductId = item.Id, Units = units });
            }
            Emit(new ItemsReservedIntegrationEvent(orderId, reservation.ExpiresAt.Value));
        }, ct);
    }

    public Task CompleteAsync(int orderId, CancellationToken ct = default) => InTransactionAsync(orderId, async () =>
    {
        var reservation = await FindAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Payment has no item reservation for order {orderId}.");
        if (reservation.State is ItemReservationState.Completed or ItemReservationState.PaymentReviewRequired)
            return;

        if (reservation.State == ItemReservationState.Active && reservation.ExpiresAt > clock.GetUtcNow())
        {
            reservation.State = ItemReservationState.Completed;
            Emit(new ItemReservationCompletedIntegrationEvent(orderId));
            return; // Units were already deducted when reserved. Never deduct a second time.
        }

        if (reservation.State == ItemReservationState.Active)
            await RestoreItemsAsync(reservation, ct);
        reservation.State = ItemReservationState.PaymentReviewRequired;
        reservation.Reason = "Payment arrived after the item reservation ended. Review the payment before fulfillment.";
        Emit(new ItemReservationPaymentReviewRequiredIntegrationEvent(orderId, reservation.Reason));
    }, ct);

    public Task ReleaseAsync(int orderId, string reason, CancellationToken ct = default) => InTransactionAsync(orderId, async () =>
    {
        var reservation = await FindAsync(orderId, ct);
        if (reservation is null)
        {
            // Cancellation can overtake the original checkout message.
            reservation = new ItemReservation { OrderId = orderId, CreatedAt = clock.GetUtcNow() };
            context.ItemReservations.Add(reservation);
        }
        else if (reservation.State != ItemReservationState.Active)
            return;
        else
            await RestoreItemsAsync(reservation, ct);

        reservation.State = ItemReservationState.Released;
        reservation.Reason = reason;
        Emit(new ItemReservationReleasedIntegrationEvent(orderId, reason));
    }, ct);

    public async Task ExpireDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var ids = await context.ItemReservations.AsNoTracking()
            .Where(x => x.State == ItemReservationState.Active && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt).Select(x => x.OrderId).Take(100).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            await InTransactionAsync(id, async () =>
            {
                var reservation = await FindAsync(id, ct);
                // Recheck after acquiring the lock: payment may have completed while we waited.
                if (reservation?.State != ItemReservationState.Active || reservation.ExpiresAt > clock.GetUtcNow())
                    return;
                await RestoreItemsAsync(reservation, ct);
                reservation.State = ItemReservationState.Released;
                reservation.Reason = "Item reservation expired.";
                Emit(new ItemReservationReleasedIntegrationEvent(id, reservation.Reason));
            }, ct);
        }
    }

    private Task<ItemReservation?> FindAsync(int id, CancellationToken ct) =>
        context.ItemReservations.Include(x => x.Items).SingleOrDefaultAsync(x => x.OrderId == id, ct);

    private async Task<CatalogItem?> LockItemAsync(int productId, CancellationToken ct) =>
        (await context.CatalogItems.FromSqlInterpolated(
            $"SELECT * FROM \"Catalog\" WHERE \"Id\" = {productId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();

    private async Task RestoreItemsAsync(ItemReservation reservation, CancellationToken ct)
    {
        foreach (var line in reservation.Items.OrderBy(x => x.ProductId))
        {
            var item = await LockItemAsync(line.ProductId, ct)
                ?? throw new InvalidOperationException($"Reserved item {line.ProductId} was removed.");
            // Exact reversal, not AddStock's warehouse-cap truncation.
            item.AvailableStock = checked(item.AvailableStock + line.Units);
        }
    }

    private void Reject(ItemReservation reservation, string reason)
    {
        reservation.State = ItemReservationState.Rejected;
        reservation.Reason = reason;
        Emit(new ItemReservationRejectedIntegrationEvent(reservation.OrderId, reason));
    }

    private void Emit(IntegrationEvent value) =>
        context.ReservationOutbox.Add(ReservationOutboxMessage.From(value, clock.GetUtcNow()));

    private async Task InTransactionAsync(int orderId, Func<Task> action, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // This service owns its scoped context. A retried transaction must read fresh state.
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            // Advisory lock also protects the first insert, when there is no row to lock yet.
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(7316, {orderId})", ct);
            await action();
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
    }
}
