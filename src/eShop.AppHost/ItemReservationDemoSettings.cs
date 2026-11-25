namespace eShop.AppHost;

public record ItemReservationDemoSettings(int DurationSeconds, int PaymentDelaySeconds, bool PaymentSucceeded)
{
    public static ItemReservationDemoSettings For(string scenario) => scenario.ToLowerInvariant() switch
    {
        "completed" => new(60, 10, true),
        "cancelled" => new(60, 20, false),
        "failed" => new(60, 10, false),
        "expired" => new(15, 25, false),
        "late-payment" => new(15, 25, true),
        _ => throw new ArgumentException("ItemReservationDemo must be completed, cancelled, failed, expired, or late-payment.")
    };
}
