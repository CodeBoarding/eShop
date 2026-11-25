namespace eShop.Catalog.API.Reservations;

public class ItemReservationWorker(
    IServiceScopeFactory scopes, IOptions<ItemReservationOptions> options, ILogger<ItemReservationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SweepIntervalSeconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ItemReservationService>().ExpireDueAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<ReservationOutboxDispatcher>().DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Item reservation sweep failed; durable work will retry on the next tick");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
