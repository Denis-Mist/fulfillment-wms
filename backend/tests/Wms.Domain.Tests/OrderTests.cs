using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static Order Make(OrderStatus status, DateTimeOffset shipBy, DateTimeOffset? shippedAt = null) =>
        new("ord_1", "ЗК-1", "123", SalesChannel.Wb, status, "org_s", "wh_1", Now.AddDays(-2), shipBy, shippedAt, "Казань",
            [new OrderLine("p1", 2, 100), new OrderLine("p2", 1, 50.5), new OrderLine("p1", 1, 100)]);

    [Fact]
    public void Totals_and_quantities()
    {
        var order = Make(OrderStatus.New, Now);
        Assert.Equal((4, 350.5, 3), (order.Units, order.Amount, order.QuantityOf("p1")));
        Assert.Equal(0, order.QuantityOf("p3"));
    }

    [Theory]
    [InlineData(OrderStatus.New, true)]
    [InlineData(OrderStatus.Reserved, true)]
    [InlineData(OrderStatus.OutOfStock, true)]
    [InlineData(OrderStatus.Picking, true)]
    [InlineData(OrderStatus.Picked, true)]
    [InlineData(OrderStatus.Packed, true)]
    [InlineData(OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void Overdue_only_for_active_past_ship_by(OrderStatus status, bool active)
    {
        Assert.Equal(active, Make(status, Now.AddHours(-1)).IsOverdue(Now));
        Assert.False(Make(status, Now.AddHours(1)).IsOverdue(Now));
    }

    [Fact]
    public void Cancelled_is_not_a_sale_and_on_time_compares_ship_by()
    {
        Assert.False(Make(OrderStatus.Cancelled, Now).CountsAsSale);
        Assert.True(Make(OrderStatus.Shipped, Now, Now.AddHours(-1)).ShippedOnTime);
        Assert.False(Make(OrderStatus.Shipped, Now, Now.AddHours(1)).ShippedOnTime);
    }

    [Fact]
    public void Shift_moves_all_dates()
    {
        var order = Make(OrderStatus.Shipped, Now, Now.AddHours(-1));
        order.ShiftTimes(TimeSpan.FromDays(3));
        Assert.Equal((Now.AddDays(1), Now.AddDays(3), Now.AddDays(3).AddHours(-1)), (order.CreatedAt, order.ShipBy, order.ShippedAt!.Value));
    }

    [Fact]
    public void Requires_lines_with_positive_quantity()
    {
        Assert.Throws<ArgumentException>(() => new Order("o", "n", null, SalesChannel.Own, OrderStatus.New, "s", "w", Now, Now, null, "c", []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderLine("p", 0, 1));
    }
}
