using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class StockTests
{
    private static StockItem Row(int qty, int reserved) => new("s", "p", "org", "wh", "loc", qty, reserved);

    [Theory]
    [InlineData(10, 0, null, StockStatus.Available, 10)]
    [InlineData(10, 4, ZoneType.Storage, StockStatus.Available, 6)]
    [InlineData(5, 5, ZoneType.Storage, StockStatus.Reserved, 0)]
    [InlineData(5, 1, ZoneType.Defect, StockStatus.Defect, 0)]
    public void Status_and_available_follow_mock(int qty, int reserved, ZoneType? zone, StockStatus status, int available)
    {
        var s = Row(qty, reserved);
        Assert.Equal((status, available), (s.StatusIn(zone), s.AvailableIn(zone)));
    }

    [Fact]
    public void Take_never_touches_reserve()
    {
        var s = Row(10, 4);
        s.Take(6);
        Assert.Equal(4, s.Quantity);
        Assert.Throws<DomainConflictException>(() => s.Take(1));
        Assert.Throws<DomainException>(() => s.Take(0));
    }

    [Fact]
    public void Recount_returns_delta_and_guards_reserve()
    {
        var s = Row(10, 3);
        Assert.Equal(-2, s.Recount(8));
        Assert.Equal(8, s.Quantity);
        Assert.Throws<DomainConflictException>(() => s.Recount(2));
        Assert.Equal(ErrorReason.Shortage, StockOperation.InventoryReason(-2));
        Assert.Equal(ErrorReason.Missort, StockOperation.InventoryReason(1));
        Assert.Null(StockOperation.InventoryReason(0));
    }

    [Fact]
    public void Empty_row_and_operation_number()
    {
        var s = Row(2, 0);
        s.Take(2);
        Assert.True(s.IsEmpty);
        Assert.Equal("OP-20160", StockOperation.NumberFor(160));
    }
}
