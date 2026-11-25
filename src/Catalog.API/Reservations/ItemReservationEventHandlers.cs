using eShop.ItemReservation.Contracts;

namespace eShop.Catalog.API.Reservations;

// Payment messages keep the established wire names, but Catalog now decides whether
// a successful payment can complete an item reservation.
public record OrderPaymentSucceededIntegrationEvent(int OrderId) : IntegrationEvent;
public record OrderPaymentFailedIntegrationEvent(int OrderId) : IntegrationEvent;

public class CompleteItemReservationOnPaymentHandler(ItemReservationService reservations)
    : IIntegrationEventHandler<OrderPaymentSucceededIntegrationEvent>
{
    public Task Handle(OrderPaymentSucceededIntegrationEvent message) => reservations.CompleteAsync(message.OrderId);
}

public class ReleaseItemReservationOnPaymentFailureHandler(ItemReservationService reservations)
    : IIntegrationEventHandler<OrderPaymentFailedIntegrationEvent>
{
    public Task Handle(OrderPaymentFailedIntegrationEvent message) =>
        reservations.ReleaseAsync(message.OrderId, "Payment failed; reserved items were released.");
}

public class ReleaseItemReservationOnCancellationHandler(ItemReservationService reservations)
    : IIntegrationEventHandler<ItemReservationReleaseRequestedIntegrationEvent>
{
    public Task Handle(ItemReservationReleaseRequestedIntegrationEvent message) =>
        reservations.ReleaseAsync(message.OrderId, "Order cancelled; reserved items were released.");
}
