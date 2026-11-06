using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class KpiMetricsTests
{
    [Fact]
    public void Empty_route_is_zero() => Assert.Equal((0, 0), KpiMetrics.RoutePair([]));

    [Fact]
    public void Single_stop_baseline_equals_optimized()
    {
        var pair = KpiMetrics.RoutePair([new RouteNode("A-01", 4, 0)]);
        Assert.Equal(8, pair.Baseline);
        Assert.Equal(pair.Baseline, pair.Optimized);
    }

    [Fact]
    public void Optimized_route_is_not_longer_than_baseline_and_can_improve()
    {
        var pair = KpiMetrics.RoutePair(
        [
            new RouteNode("A-01", 20, 20),
            new RouteNode("B-01", 1, 0),
            new RouteNode("C-01", 20, 0),
        ]);
        Assert.True(pair.Optimized <= pair.Baseline);
        Assert.True(pair.Baseline - pair.Optimized > 1);
        Assert.True(KpiMetrics.ReductionPct(pair.Baseline, pair.Optimized) > 0);
    }

    [Fact]
    public void Reduction_and_error_rate_are_zero_without_a_base()
    {
        Assert.Equal(0, KpiMetrics.ReductionPct(0, 10));
        Assert.Equal(0, KpiMetrics.ErrorRate(5, 0));
        Assert.Equal(2.5, KpiMetrics.ErrorRate(5, 200));
        Assert.Equal(0, KpiMetrics.AverageOrZero([]));
        Assert.Equal(2, KpiMetrics.AverageOrZero([1, 3]));
    }

    [Fact]
    public void Utilization_is_work_over_scheduled_shift_minutes()
    {
        Assert.Equal(0, KpiMetrics.Utilization(100, 0));
        Assert.Equal(50, KpiMetrics.Utilization(6 * 60, 1));
        Assert.Equal(100, KpiMetrics.Utilization(100_000, 1));
    }
}
