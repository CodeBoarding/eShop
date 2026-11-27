using eShop.Catalog.API.Infrastructure;
using eShop.Catalog.API.Model;
using eShop.Catalog.API.Reservations;
using eShop.EventBus.Abstractions;
using eShop.EventBus.Events;
using eShop.ItemReservation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace eShop.ItemReservation.FunctionalTests;

[TestClass, DoNotParallelize, TestCategory("Docker")]
public class ItemReservationLifecycleTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly DisposablePostgres Database = new();
    private readonly ManualClock clock = new();
    private static readonly ItemReservationRequest[] OneItem = [new(1, 1)];

    [ClassInitialize]
    public static async Task StartDatabase(TestContext _)
    {
        try
        {
            await Database.StartAsync();
            await using var context = NewContext();
            await context.Database.MigrateAsync(_.CancellationToken);
        }
        catch { await Database.DisposeAsync(); throw; }
    }

    [ClassCleanup]
    public static async Task StopDatabase() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task Seed()
    {
        await using var context = NewContext();
        await context.Database.ExecuteSqlRawAsync("""
            TRUNCATE "ReservationOutbox", "ReservedItems", "ItemReservations", "Catalog", "CatalogBrand", "CatalogType" CASCADE
            """);
        context.CatalogBrands.Add(new CatalogBrand("Demo") { Id = 1 });
        context.CatalogTypes.Add(new CatalogType("Outdoor") { Id = 1 });
        context.CatalogItems.AddRange(
            new CatalogItem("Last tent") { Id = 1, AvailableStock = 1, MaxStockThreshold = 100, CatalogBrandId = 1, CatalogTypeId = 1 },
            new CatalogItem("Backpack") { Id = 2, AvailableStock = 5, MaxStockThreshold = 100, CatalogBrandId = 1, CatalogTypeId = 1 });
        await context.SaveChangesAsync();
    }

    [TestMethod]
    public async Task TwoCheckoutsForLastItem_OnlyOneSucceeds()
    {
        await Task.WhenAll(Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken)), Run(s => s.ReserveAsync(102, OneItem, ct: TestContext.CancellationToken)));
        await using var context = NewContext();
        Assert.AreEqual(1, await context.ItemReservations.CountAsync(x => x.State == ItemReservationState.Active));
        Assert.AreEqual(1, await context.ItemReservations.CountAsync(x => x.State == ItemReservationState.Rejected));
        Assert.AreEqual(0, await Quantity(1));
    }

    [TestMethod]
    public async Task PartialAvailability_DoesNotReserveAnyItems()
    {
        await Run(s => s.ReserveAsync(101, [new(1, 1), new(2, 6)], ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(5, await Quantity(2));
        Assert.AreEqual(ItemReservationState.Rejected, await State(101));
    }

    [TestMethod]
    public async Task MissingProduct_RejectsWholeBasket()
    {
        await Run(s => s.ReserveAsync(101, [new(1, 1), new(999, 1)], ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Rejected, await State(101));
    }

    [TestMethod]
    public async Task DuplicateLines_AreSummedBeforeCheckingAvailability()
    {
        await Run(s => s.ReserveAsync(101, [new(1, 1), new(1, 1)], ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Rejected, await State(101));
    }

    [TestMethod]
    public async Task DuplicateReserveAndPayment_DoNotDeductTwice()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        await Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken));
        await Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken));
        Assert.AreEqual(0, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Completed, await State(101));
        await using var context = NewContext();
        Assert.AreEqual(2, await context.ReservationOutbox.CountAsync());
    }

    [TestMethod]
    public async Task CancellationAndPaymentFailure_ReleaseExactlyOnce()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        await Task.WhenAll(Run(s => s.ReleaseAsync(101, "Cancelled", ct: TestContext.CancellationToken)), Run(s => s.ReleaseAsync(101, "Payment failed", ct: TestContext.CancellationToken)));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Released, await State(101));
    }

    [TestMethod]
    public async Task CancellationBeforeCheckout_PreventsLaterReservation()
    {
        await Run(s => s.ReleaseAsync(101, "Cancelled", ct: TestContext.CancellationToken));
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Released, await State(101));
    }

    [TestMethod]
    public async Task CompletedReservation_CannotBeReleasedByLateCancellationOrFailure()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        await Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken));
        await Run(s => s.ReleaseAsync(101, "Cancelled", ct: TestContext.CancellationToken));
        clock.Advance(TimeSpan.FromHours(1));
        await Run(s => s.ExpireDueAsync(ct: TestContext.CancellationToken));
        Assert.AreEqual(0, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Completed, await State(101));
    }

    [TestMethod]
    public async Task ExpiryAfterRestart_RestoresItemsFromPersistedReservation()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        clock.Advance(TimeSpan.FromMinutes(10));
        // Every Run creates a new context/service, as after a process restart.
        await Run(s => s.ExpireDueAsync(ct: TestContext.CancellationToken));
        await Run(s => s.ExpireDueAsync(ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Released, await State(101));
    }

    [TestMethod]
    public async Task PaymentAtDeadlineWithoutSweep_RequiresReviewAndRestoresItems()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        clock.Advance(TimeSpan.FromMinutes(10));
        await Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken));
        await Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.PaymentReviewRequired, await State(101));
    }

    [TestMethod]
    public async Task LatePaymentAfterItemsWereReallocated_DoesNotConsumeAnotherOrdersItems()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        clock.Advance(TimeSpan.FromMinutes(10));
        await Run(s => s.ExpireDueAsync(ct: TestContext.CancellationToken));
        await Run(s => s.ReserveAsync(102, OneItem, ct: TestContext.CancellationToken));
        await Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken));
        Assert.AreEqual(0, await Quantity(1));
        Assert.AreEqual(ItemReservationState.Active, await State(102));
        Assert.AreEqual(ItemReservationState.PaymentReviewRequired, await State(101));
    }

    [TestMethod]
    public async Task ConcurrentExpiryAndPayment_NeverOversellsOrRestoresTwice()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        clock.Advance(TimeSpan.FromMinutes(10));
        await Task.WhenAll(Run(s => s.ExpireDueAsync(ct: TestContext.CancellationToken)), Run(s => s.CompleteAsync(101, ct: TestContext.CancellationToken)));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(ItemReservationState.PaymentReviewRequired, await State(101));
    }

    [TestMethod]
    public async Task CompetingMultiItemReservations_LockInConsistentOrder()
    {
        await Task.WhenAll(Run(s => s.ReserveAsync(101, [new(1, 1), new(2, 1)], ct: TestContext.CancellationToken)),
            Run(s => s.ReserveAsync(102, [new(2, 1), new(1, 1)], ct: TestContext.CancellationToken)));
        Assert.AreEqual(0, await Quantity(1));
        Assert.AreEqual(4, await Quantity(2));
    }

    [TestMethod]
    public async Task RejectedInputs_DoNotChangeQuantities()
    {
        ItemReservationRequest[][] invalid = [[], [new(1, 0)], [new(1, -1)], [new(-1, 1)],
            [new(1, int.MaxValue), new(1, int.MaxValue)], Enumerable.Repeat(new ItemReservationRequest(2, 1), 101).ToArray()];
        for (var i = 0; i < invalid.Length; i++)
            await Run(s => s.ReserveAsync(101 + i, invalid[i], ct: TestContext.CancellationToken));
        Assert.AreEqual(1, await Quantity(1));
        Assert.AreEqual(5, await Quantity(2));
    }

    [TestMethod]
    public async Task FailedPublication_IsRetriedFromDurableOutboxAfterRestart()
    {
        await Run(s => s.ReserveAsync(101, OneItem, ct: TestContext.CancellationToken));
        await using (var context = NewContext())
        {
            await Assert.ThrowsExactlyAsync<IOException>(() =>
                new ReservationOutboxDispatcher(context, new RecordingBus { Fail = true }, clock).DispatchPendingAsync(TestContext.CancellationToken));
        }
        var recoveredBus = new RecordingBus();
        await using (var context = NewContext())
            await new ReservationOutboxDispatcher(context, recoveredBus, clock).DispatchPendingAsync(TestContext.CancellationToken);
        await using (var context = NewContext())
            await new ReservationOutboxDispatcher(context, recoveredBus, clock).DispatchPendingAsync(TestContext.CancellationToken);
        Assert.HasCount(1, recoveredBus.Messages);
        var message = (ItemsReservedIntegrationEvent)recoveredBus.Messages.Single();
        Assert.AreEqual(101, message.OrderId);
        Assert.AreEqual(clock.GetUtcNow().AddMinutes(10), message.ExpiresAt);
        Assert.AreEqual(0, await Quantity(1));
    }

    [TestMethod]
    public async Task StaleCatalogEdit_CannotOverwriteAReservation()
    {
        await using var editor = NewContext();
        var item = (await editor.CatalogItems.FindAsync(1))!;
        item.AvailableStock = 10; // An admin edits a stale copy while checkout reserves the last unit.
        await Run(service => service.ReserveAsync(101, OneItem, TestContext.CancellationToken));
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => editor.SaveChangesAsync(TestContext.CancellationToken));
        Assert.AreEqual(0, await Quantity(1));
    }

    [TestMethod]
    public async Task MigrationModelMatchesReservationSchema()
    {
        await using var context = NewContext();
        Assert.IsFalse(context.Database.HasPendingModelChanges());
        var options = new DbContextOptionsBuilder<eShop.Ordering.Infrastructure.OrderingContext>()
            .UseNpgsql(Database.ConnectionString).Options;
        await using var ordering = new eShop.Ordering.Infrastructure.OrderingContext(options);
        await ordering.Database.MigrateAsync(TestContext.CancellationToken);
        Assert.IsFalse(ordering.Database.HasPendingModelChanges());
    }

    private async Task Run(Func<ItemReservationService, Task> action)
    {
        await using var context = NewContext();
        await action(new ItemReservationService(context, clock, Options.Create(new ItemReservationOptions())));
    }

    private static CatalogContext NewContext() => new(
        new DbContextOptionsBuilder<CatalogContext>().UseNpgsql(Database.ConnectionString, x => x.UseVector()).Options,
        new ConfigurationBuilder().Build());

    private static async Task<int> Quantity(int id)
    {
        await using var context = NewContext();
        return (await context.CatalogItems.FindAsync(id))!.AvailableStock;
    }

    private static async Task<ItemReservationState> State(int orderId)
    {
        await using var context = NewContext();
        return (await context.ItemReservations.FindAsync(orderId))!.State;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class RecordingBus : IEventBus
    {
        public bool Fail { get; init; }
        public List<IntegrationEvent> Messages { get; } = [];
        public Task PublishAsync(IntegrationEvent message)
        {
            if (Fail) throw new IOException("Simulated broker outage");
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
