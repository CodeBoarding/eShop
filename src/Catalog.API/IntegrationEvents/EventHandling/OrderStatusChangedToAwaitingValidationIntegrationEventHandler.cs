using eShop.Catalog.API.Reservations;

namespace eShop.Catalog.API.IntegrationEvents.EventHandling;

public class OrderStatusChangedToAwaitingValidationIntegrationEventHandler(ItemReservationService reservations)
    : IIntegrationEventHandler<OrderStatusChangedToAwaitingValidationIntegrationEvent>
{
    public Task Handle(OrderStatusChangedToAwaitingValidationIntegrationEvent message) =>
        reservations.ReserveAsync(message.OrderId,
            message.OrderStockItems.Select(x => new ItemReservationRequest(x.ProductId, x.Units)));
}
