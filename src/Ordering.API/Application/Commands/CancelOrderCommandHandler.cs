namespace eShop.Ordering.API.Application.Commands;

// Regular CommandHandler
public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, bool>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingIntegrationEventService _events;

    public CancelOrderCommandHandler(IOrderRepository orderRepository, IOrderingIntegrationEventService events)
    {
        _orderRepository = orderRepository;
        _events = events;
    }

    /// <summary>
    /// Handler which processes the command when
    /// customer executes cancel order from app
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    public async Task<bool> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var orderToUpdate = await _orderRepository.GetAsync(command.OrderNumber);
        if (orderToUpdate == null)
        {
            return false;
        }

        if (orderToUpdate.RequestItemReservationCancellation())
        {
            await _events.AddAndSaveEventAsync(
                new eShop.ItemReservation.Contracts.ItemReservationReleaseRequestedIntegrationEvent(command.OrderNumber));
        }
        return await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }
}


// Use for Idempotency in Command process
public class CancelOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CancelOrderCommand, bool>
{
    public CancelOrderIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CancelOrderCommand, bool>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override bool CreateResultForDuplicateRequest()
    {
        return true; // Ignore duplicate requests for processing order.
    }
}
