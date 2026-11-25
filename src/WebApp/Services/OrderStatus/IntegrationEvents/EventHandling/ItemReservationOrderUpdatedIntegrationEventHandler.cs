using eShop.EventBus.Abstractions;
using eShop.ItemReservation.Contracts;

namespace eShop.WebApp.Services.OrderStatus.IntegrationEvents.EventHandling;

public class ItemReservationOrderUpdatedIntegrationEventHandler(OrderStatusNotificationService notifications)
    : IIntegrationEventHandler<ItemReservationOrderUpdatedIntegrationEvent>
{
    public Task Handle(ItemReservationOrderUpdatedIntegrationEvent message) =>
        notifications.NotifyOrderStatusChangedAsync(message.BuyerIdentityGuid);
}
