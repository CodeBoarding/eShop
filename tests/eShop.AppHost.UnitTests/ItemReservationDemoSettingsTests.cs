using eShop.AppHost;

namespace eShop.AppHost.UnitTests;

[TestClass]
public class ItemReservationDemoSettingsTests
{
    [TestMethod]
    [DataRow("completed", 60, 10, true)]
    [DataRow("cancelled", 60, 20, false)]
    [DataRow("failed", 60, 10, false)]
    [DataRow("expired", 15, 25, false)]
    [DataRow("late-payment", 15, 25, true)]
    public void ScenariosUsePredictableTiming(string scenario, int duration, int delay, bool succeeded)
    {
        Assert.AreEqual(new ItemReservationDemoSettings(duration, delay, succeeded), ItemReservationDemoSettings.For(scenario));
    }

    [TestMethod]
    public void UnknownScenarioFailsInsteadOfSilentlyChangingBehavior() =>
        Assert.ThrowsExactly<ArgumentException>(() => ItemReservationDemoSettings.For("typo"));
}
