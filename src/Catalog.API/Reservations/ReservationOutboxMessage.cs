using System.Text.Json;
using eShop.ItemReservation.Contracts;

namespace eShop.Catalog.API.Reservations;

// Stored in the same transaction as the item quantities and reservation state.
// Kept separate from the legacy publish-once integration log so startup can replay it.
public class ReservationOutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public static ReservationOutboxMessage From(IntegrationEvent value, DateTimeOffset now) => new()
    {
        Id = value.Id,
        EventType = value.GetType().Name,
        Payload = JsonSerializer.Serialize(value, value.GetType()),
        CreatedAt = now
    };

    public IntegrationEvent ToEvent() => EventType switch
    {
        nameof(ItemsReservedIntegrationEvent) => JsonSerializer.Deserialize<ItemsReservedIntegrationEvent>(Payload)!,
        nameof(ItemReservationCompletedIntegrationEvent) => JsonSerializer.Deserialize<ItemReservationCompletedIntegrationEvent>(Payload)!,
        nameof(ItemReservationReleasedIntegrationEvent) => JsonSerializer.Deserialize<ItemReservationReleasedIntegrationEvent>(Payload)!,
        nameof(ItemReservationRejectedIntegrationEvent) => JsonSerializer.Deserialize<ItemReservationRejectedIntegrationEvent>(Payload)!,
        nameof(ItemReservationPaymentReviewRequiredIntegrationEvent) => JsonSerializer.Deserialize<ItemReservationPaymentReviewRequiredIntegrationEvent>(Payload)!,
        _ => throw new InvalidOperationException($"Unknown reservation event {EventType}")
    };
}
