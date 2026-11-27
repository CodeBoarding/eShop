using System.ComponentModel.DataAnnotations;

namespace eShop.Catalog.API.Reservations;

public class ItemReservationOptions
{
    [Range(1, 3600)]
    public int DurationSeconds { get; set; } = 600;

    [Range(1, 60)]
    public int SweepIntervalSeconds { get; set; } = 2;
}
