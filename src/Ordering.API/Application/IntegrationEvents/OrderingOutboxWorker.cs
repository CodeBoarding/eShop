using eShop.IntegrationEventLogEF;

namespace eShop.Ordering.API.Application.IntegrationEvents;

// Recovers payment requests and cancellation commands if the process exits after commit.
public class OrderingOutboxWorker(IServiceScopeFactory scopes, ILogger<OrderingOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<OrderingContext>();
                var events = scope.ServiceProvider.GetRequiredService<IOrderingIntegrationEventService>();
                var cutoff = DateTime.UtcNow.AddSeconds(-5);
                var transactions = await context.Set<IntegrationEventLogEntry>().AsNoTracking()
                    .Where(x => x.State != EventStateEnum.Published && x.CreationTime < cutoff)
                    .Select(x => x.TransactionId).Distinct().Take(100).ToArrayAsync(stoppingToken);
                foreach (var transaction in transactions)
                    await events.PublishEventsThroughEventBusAsync(transaction);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Ordering outbox retry failed; retrying on the next tick"); }
        }
    }
}
