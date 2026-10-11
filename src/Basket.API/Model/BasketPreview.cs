namespace eShop.Basket.API.Model;

/// <summary>A read-only line estimate. Creating a preview never writes a basket or reserves stock.</summary>
public record BasketPreview(int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal TotalPrice)
{
    public static BasketPreview Create(BasketItem item)
    {
        Validator.ValidateObject(item, new ValidationContext(item), validateAllProperties: true);

        return new BasketPreview(item.ProductId, item.ProductName, item.Quantity,
            item.UnitPrice, item.UnitPrice * item.Quantity);
    }
}
