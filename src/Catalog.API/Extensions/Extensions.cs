using eShop.Catalog.API.Services;
using eShop.Catalog.API.Reservations;
using eShop.ItemReservation.Contracts;

public static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        // Avoid loading full database config and migrations if startup
        // is being invoked from build-time OpenAPI generation
        if (builder.Environment.IsBuild())
        {
            builder.Services.AddDbContext<CatalogContext>();
            return;
        }

        builder.AddNpgsqlDbContext<CatalogContext>("catalogdb", configureDbContextOptions: dbContextOptionsBuilder =>
        {
            dbContextOptionsBuilder.UseNpgsql(builder =>
            {
                builder.UseVector();
            });
        });

        // REVIEW: This is done for development ease but shouldn't be here in production
        builder.Services.AddMigration<CatalogContext, CatalogContextSeed>();

        // Add the integration services that consume the DbContext
        builder.Services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<CatalogContext>>();

        builder.Services.AddTransient<ICatalogIntegrationEventService, CatalogIntegrationEventService>();

        builder.AddRabbitMqEventBus("eventbus")
               .AddSubscription<OrderStatusChangedToAwaitingValidationIntegrationEvent, OrderStatusChangedToAwaitingValidationIntegrationEventHandler>()
               .AddSubscription<OrderPaymentSucceededIntegrationEvent, CompleteItemReservationOnPaymentHandler>()
               .AddSubscription<OrderPaymentFailedIntegrationEvent, ReleaseItemReservationOnPaymentFailureHandler>()
               .AddSubscription<ItemReservationReleaseRequestedIntegrationEvent, ReleaseItemReservationOnCancellationHandler>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOptions<ItemReservationOptions>().BindConfiguration("ItemReservation")
            .ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddScoped<ItemReservationService>();
        builder.Services.AddScoped<ReservationOutboxDispatcher>();
        builder.Services.AddHostedService<ItemReservationWorker>();

        builder.Services.AddOptions<CatalogOptions>()
            .BindConfiguration(nameof(CatalogOptions));

        if (builder.Configuration["OllamaEnabled"] is string ollamaEnabled && bool.Parse(ollamaEnabled))
        {
            builder.AddOllamaApiClient("embedding")
                .AddEmbeddingGenerator();
        }
        else if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("textEmbeddingModel")))
        {
            builder.AddOpenAIClientFromConfiguration("textEmbeddingModel")
                .AddEmbeddingGenerator();
        }

        builder.Services.AddScoped<ICatalogAI, CatalogAI>();
    }
}
