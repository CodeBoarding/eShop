using System.ComponentModel.DataAnnotations;
using eShop.Basket.API.Model;

namespace eShop.Basket.UnitTests;

[TestClass]
public class BasketPreviewTests
{
    [TestMethod]
    public void PreviewUsesBasketQuantityAndDecimalPriceWithoutChangingTheItem()
    {
        var item = new BasketItem
        {
            ProductId = 42,
            ProductName = "Trail shoes",
            Quantity = 3,
            UnitPrice = 19.95m
        };

        var preview = BasketPreview.Create(item);

        Assert.AreEqual(new BasketPreview(42, "Trail shoes", 3, 19.95m, 59.85m), preview);
        Assert.AreEqual(3, item.Quantity);
        Assert.AreEqual(19.95m, item.UnitPrice);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void PreviewReusesBasketItemQuantityValidation(int quantity)
    {
        var item = new BasketItem { ProductId = 42, Quantity = quantity, UnitPrice = 19.95m };

        var exception = Assert.Throws<ValidationException>(() => BasketPreview.Create(item));

        Assert.AreEqual("Invalid number of units", exception.Message);
    }

    [TestMethod]
    public void PreviewAcceptsOneFreeItem()
    {
        var preview = BasketPreview.Create(new BasketItem
        {
            ProductId = 7,
            ProductName = "Gift",
            Quantity = 1,
            UnitPrice = 0m
        });

        Assert.AreEqual(new BasketPreview(7, "Gift", 1, 0m, 0m), preview);
    }
}
