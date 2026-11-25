namespace eShop.PaymentProcessor;

public class PaymentOptions
{
    [System.ComponentModel.DataAnnotations.Range(0, 3600)]
    public int DelaySeconds { get; set; }

    public bool PaymentSucceeded { get; set; }
}

