using eShop.ItemReservation.Contracts;

namespace eShop.Ordering.API.Application.IntegrationEvents.EventHandling;

public class ItemsReservedIntegrationEventHandler(IMediator mediator) : IIntegrationEventHandler<ItemsReservedIntegrationEvent>
{
    public async Task Handle(ItemsReservedIntegrationEvent message) =>
        await mediator.Send(new ApplyItemReservationOutcomeCommand(message.OrderId, ItemReservationOutcome.Reserved, message.ExpiresAt));
}

public class ItemReservationCompletedIntegrationEventHandler(IMediator mediator) : IIntegrationEventHandler<ItemReservationCompletedIntegrationEvent>
{
    public async Task Handle(ItemReservationCompletedIntegrationEvent message) =>
        await mediator.Send(new ApplyItemReservationOutcomeCommand(message.OrderId, ItemReservationOutcome.Completed));
}

public class ItemReservationReleasedIntegrationEventHandler(IMediator mediator) : IIntegrationEventHandler<ItemReservationReleasedIntegrationEvent>
{
    public async Task Handle(ItemReservationReleasedIntegrationEvent message) =>
        await mediator.Send(new ApplyItemReservationOutcomeCommand(message.OrderId, ItemReservationOutcome.Released, Reason: message.Reason));
}

public class ItemReservationRejectedIntegrationEventHandler(IMediator mediator) : IIntegrationEventHandler<ItemReservationRejectedIntegrationEvent>
{
    public async Task Handle(ItemReservationRejectedIntegrationEvent message) =>
        await mediator.Send(new ApplyItemReservationOutcomeCommand(message.OrderId, ItemReservationOutcome.Rejected, Reason: message.Reason));
}

public class ItemReservationPaymentReviewRequiredIntegrationEventHandler(IMediator mediator) : IIntegrationEventHandler<ItemReservationPaymentReviewRequiredIntegrationEvent>
{
    public async Task Handle(ItemReservationPaymentReviewRequiredIntegrationEvent message) =>
        await mediator.Send(new ApplyItemReservationOutcomeCommand(message.OrderId, ItemReservationOutcome.PaymentReviewRequired, Reason: message.Reason));
}
