using eShop.ItemReservation.Contracts;

namespace eShop.Ordering.API.Application.Commands;

public enum ItemReservationOutcome { Reserved, Completed, Released, Rejected, PaymentReviewRequired }

public record ApplyItemReservationOutcomeCommand(
    int OrderNumber, ItemReservationOutcome Outcome, DateTimeOffset? ExpiresAt = null, string Reason = "") : IRequest<bool>;

public class ApplyItemReservationOutcomeCommandHandler(
    IOrderRepository orders, IBuyerRepository buyers, IOrderingIntegrationEventService events)
    : IRequestHandler<ApplyItemReservationOutcomeCommand, bool>
{
    public async Task<bool> Handle(ApplyItemReservationOutcomeCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(command.OrderNumber)
            ?? throw new InvalidOperationException($"Reservation refers to missing order {command.OrderNumber}.");
        switch (command.Outcome)
        {
            case ItemReservationOutcome.Reserved:
                order.SetItemsReserved(command.ExpiresAt ?? throw new ArgumentException("Reservation requires an expiry."));
                break;
            case ItemReservationOutcome.Completed:
                order.SetPaidStatus();
                break;
            case ItemReservationOutcome.Released:
            case ItemReservationOutcome.Rejected:
                order.SetItemReservationReleased(command.Reason);
                break;
            case ItemReservationOutcome.PaymentReviewRequired:
                order.SetItemReservationPaymentReviewRequired(command.Reason);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
        var buyer = await buyers.FindByIdAsync(order.BuyerId!.Value);
        await events.AddAndSaveEventAsync(new ItemReservationOrderUpdatedIntegrationEvent(buyer.IdentityGuid));
        return await orders.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }
}
