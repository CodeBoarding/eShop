using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.UnitTests.Domain;

[TestClass]
public class ItemReservationOrderTests
{
    [TestMethod]
    public void ReservedItems_StartPaymentAndExposeDeadline()
    {
        var order = NewOrder();
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        order.SetItemsReserved(expires);
        Assert.AreEqual(OrderStatus.StockConfirmed, order.OrderStatus);
        Assert.AreEqual(expires, order.ItemReservationExpiresAt);
        Assert.AreEqual(1, order.DomainEvents.OfType<OrderStatusChangedToStockConfirmedDomainEvent>().Count());
        order.SetItemsReserved(expires);
        Assert.AreEqual(1, order.DomainEvents.OfType<OrderStatusChangedToStockConfirmedDomainEvent>().Count());
    }

    [TestMethod]
    public void CancellationIsARequest_UntilCatalogConfirmsRelease()
    {
        var order = NewOrder();
        Assert.IsTrue(order.RequestItemReservationCancellation());
        Assert.AreEqual(OrderStatus.AwaitingValidation, order.OrderStatus);
        Assert.IsFalse(order.RequestItemReservationCancellation());
        order.SetItemsReserved(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.IsFalse(order.DomainEvents.OfType<OrderStatusChangedToStockConfirmedDomainEvent>().Any());
        order.SetItemReservationReleased("Cancelled");
        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
    }

    [TestMethod]
    public void CompletionCanWinAgainstCancellationRequest()
    {
        var order = NewOrder();
        order.SetItemsReserved(DateTimeOffset.UtcNow.AddMinutes(10));
        order.RequestItemReservationCancellation();
        order.SetPaidStatus();
        order.SetItemReservationReleased("Late release notification");
        Assert.AreEqual(OrderStatus.Paid, order.OrderStatus);
        Assert.IsFalse(order.RequestItemReservationCancellation());
    }

    [TestMethod]
    public void LatePaymentRequiresReview_AndCannotShip()
    {
        var order = NewOrder();
        order.SetItemReservationReleased("Expired");
        order.SetItemReservationPaymentReviewRequired("Late payment");
        order.SetItemReservationReleased("Duplicate expiry");
        order.SetItemsReserved(DateTimeOffset.UtcNow.AddMinutes(10));
        order.SetPaidStatus();
        Assert.AreEqual(OrderStatus.PaymentReviewRequired, order.OrderStatus);
        Assert.AreEqual("Late payment", order.Description);
        Assert.ThrowsExactly<OrderingDomainException>(() => order.SetShippedStatus());
        Assert.ThrowsExactly<OrderingDomainException>(() => order.SetCancelledStatus());
    }

    [TestMethod]
    public void ReorderedReservedNotification_CannotReviveCancelledOrder()
    {
        var order = NewOrder();
        order.SetItemReservationReleased("Expired");
        order.SetItemsReserved(DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.AreEqual(OrderStatus.Cancelled, order.OrderStatus);
        Assert.IsFalse(order.DomainEvents.OfType<OrderStatusChangedToStockConfirmedDomainEvent>().Any());
    }

    private static Order NewOrder()
    {
        var order = new Order("buyer", "Demo", new Address("street", "city", "state", "country", "zip"),
            1, "0000", "000", "Demo", DateTime.UtcNow.AddYears(1));
        order.AddOrderItem(1, "Tent", 10, 0, "", 1);
        order.SetAwaitingValidationStatus();
        order.ClearDomainEvents();
        return order;
    }
}
