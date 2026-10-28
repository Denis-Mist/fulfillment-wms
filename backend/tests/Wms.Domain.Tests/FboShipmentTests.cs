using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class FboShipmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static FboShipment Draft() => FboShipment.Create("fbo_1", "FBO-3101", FboMarketplace.Wb, "WB Казань", "org_s", "wh_1", Now,
        [new FboLine("p1", 50), new FboLine("p2", 31)], "Селлер", Now);

    [Fact]
    public void Full_lifecycle_tracks_history_and_packing()
    {
        var r = Draft();
        Assert.Equal(81, r.Units);
        Assert.Equal(3, r.Boxes);
        r.Submit("1234567", "Селлер", Now);
        Assert.Equal("WB-GI-1234567", r.SupplyId);
        Assert.Throws<DomainConflictException>(() => r.Submit("1", "Селлер", Now));
        Assert.Throws<DomainConflictException>(() => r.Advance(FboStatus.Ready, "ФЦ", Now));
        r.Advance(FboStatus.Assembling, "ФЦ", Now);
        Assert.Throws<DomainConflictException>(() => r.Cancel("Селлер", Now));
        r.Advance(FboStatus.Ready, "ФЦ", Now);
        Assert.Equal(81, r.Packed);
        r.Advance(FboStatus.Shipped, "ФЦ", Now);
        Assert.Equal([FboStatus.Draft, FboStatus.Confirmed, FboStatus.Assembling, FboStatus.Ready, FboStatus.Shipped], r.History.Select(h => h.Status));
        Assert.Throws<DomainConflictException>(() => r.Advance(FboStatus.Shipped, "ФЦ", Now));
    }

    [Fact]
    public void Cancel_before_assembling_and_validation()
    {
        var r = Draft();
        r.Cancel("Селлер", Now);
        Assert.Equal(FboStatus.Cancelled, r.Status);
        Assert.Throws<DomainValidationException>(() => FboShipment.Create("x", "n", FboMarketplace.Ozon, "WB Казань", "s", "w", Now, [new FboLine("p", 1)], "b", Now));
        Assert.Throws<DomainValidationException>(() => FboShipment.Create("x", "n", FboMarketplace.Ozon, "Ozon Тверь", "s", "w", Now, [], "b", Now));
        Assert.Throws<DomainValidationException>(() => new FboLine("p", 0));
    }

    [Fact]
    public void Ozon_supply_prefix_and_shift()
    {
        var r = FboShipment.Create("x", "n", FboMarketplace.Ozon, "Ozon Тверь", "s", "w", Now, [new FboLine("p", 1)], "b", Now);
        r.Submit("7654321", "b", Now);
        Assert.Equal("OZ-7654321", r.SupplyId);
        r.ShiftTimes(TimeSpan.FromDays(1));
        Assert.Equal(Now.AddDays(1), r.CreatedAt);
        Assert.All(r.History, h => Assert.Equal(Now.AddDays(1), h.At));
    }
}
