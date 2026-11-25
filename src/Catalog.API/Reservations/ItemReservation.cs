namespace eShop.Catalog.API.Reservations;

public enum ItemReservationState { Active, Completed, Released, Rejected, PaymentReviewRequired }

public class ItemReservation
{
    public int OrderId { get; set; }
    public ItemReservationState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Reason { get; set; } = "";
    public List<ReservedItem> Items { get; set; } = [];
}

public class ReservedItem
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Units { get; set; }
}

public record ItemReservationRequest(int ProductId, int Units);
