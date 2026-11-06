using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class DemandHistoryTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 1, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildUtc_bins_by_utc_day_fills_gaps_and_excludes_today()
    {
        var days = DemandHistory.BuildUtc(Today, 4,
        [
            new DemandEvent(Start.AddDays(-3).AddHours(8), 2, 5, 2),
            new DemandEvent(Start.AddDays(-3).AddHours(20), 1, 1, 1),
            new DemandEvent(Start.AddDays(-1).AddHours(1), 4, 10, 3),
            new DemandEvent(Start.AddHours(3), 99, 99, 99),
            new DemandEvent(Start.AddDays(-5), 7, 7, 7),
        ]);

        Assert.Equal(4, days.Count);
        Assert.Equal([Start.AddDays(-4), Start.AddDays(-3), Start.AddDays(-2), Start.AddDays(-1)], days.Select(d => d.Date));
        Assert.Equal([(0, 0, 0), (3, 6, 3), (0, 0, 0), (4, 10, 3)], days.Select(d => (d.Orders, d.Units, d.PickLines)));
    }

    [Fact]
    public void Average_units_prefers_order_units_then_pick_lines_then_fallback()
    {
        var withUnits = DemandHistory.BuildUtc(Start, 2,
        [
            new DemandEvent(Start.AddDays(-1), 2, 8, 100),
        ]);
        Assert.Equal(4, DemandHistory.AverageUnitsPerOrder(withUnits));

        var pickOnly = DemandHistory.BuildUtc(Start, 2,
        [
            new DemandEvent(Start.AddDays(-1), 4, 0, 10),
        ]);
        Assert.Equal(2.5, DemandHistory.AverageUnitsPerOrder(pickOnly));

        var empty = DemandHistory.BuildUtc(Start, 2, []);
        Assert.Equal(2.6, DemandHistory.AverageUnitsPerOrder(empty));
    }

    [Fact]
    public void BuildUtc_ignores_non_positive_window() =>
        Assert.Empty(DemandHistory.BuildUtc(Start, 0, [new DemandEvent(Start, 1, 1, 1)]));
}
