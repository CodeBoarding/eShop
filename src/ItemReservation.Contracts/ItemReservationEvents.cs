using eShop.EventBus.Events;

namespace eShop.ItemReservation.Contracts;

// These are facts owned by Catalog. Ordering must not infer them from payment alone.
public record ItemsReservedIntegrationEvent(int OrderId, DateTimeOffset ExpiresAt) : IntegrationEvent;
public record ItemReservationCompletedIntegrationEvent(int OrderId) : IntegrationEvent;
public record ItemReservationReleasedIntegrationEvent(int OrderId, string Reason) : IntegrationEvent;
public record ItemReservationRejectedIntegrationEvent(int OrderId, string Reason) : IntegrationEvent;
public record ItemReservationPaymentReviewRequiredIntegrationEvent(int OrderId, string Reason) : IntegrationEvent;

// A request, not a cancellation fact: completion may already have won the race.
public record ItemReservationReleaseRequestedIntegrationEvent(int OrderId) : IntegrationEvent;

// Buyer-scoped notification; the browser re-reads Ordering's projection.
public record ItemReservationOrderUpdatedIntegrationEvent(string BuyerIdentityGuid) : IntegrationEvent;
