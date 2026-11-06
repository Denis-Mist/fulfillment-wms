using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class BatchOptimizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static BatchCandidate Order(string id, double volume, DateTimeOffset shipBy, params RouteNode[] stops) =>
        new(id, shipBy, volume, stops);

    [Fact]
    public void Empty_input_yields_no_batches() =>
        Assert.Empty(BatchOptimizer.Plan([]));

    [Fact]
    public void Single_order_forms_one_batch_even_if_it_exceeds_tote()
    {
        var plan = Assert.Single(BatchOptimizer.Plan([Order("o1", 100, Now, new RouteNode("A-01", 4, 0))], toteCapacityLiters: 60));
        Assert.Equal(["o1"], plan.OrderIds);
        Assert.Equal(100, plan.VolumeLiters);
        Assert.Equal(60, plan.ToteCapacityLiters);
        Assert.Equal(8, plan.OptimizedLength);
        Assert.Equal(plan.OptimizedLength, plan.BaselineLength);
    }

    [Fact]
    public void Tote_capacity_splits_orders_that_do_not_fit_together()
    {
        var a = Order("a", 40, Now, new RouteNode("A-01", 1, 0));
        var b = Order("b", 40, Now.AddMinutes(10), new RouteNode("A-02", 2, 0));
        var plans = BatchOptimizer.Plan([a, b], toteCapacityLiters: 60);
        Assert.Equal(2, plans.Count);
        Assert.Equal(["a"], plans[0].OrderIds);
        Assert.Equal(["b"], plans[1].OrderIds);
    }

    [Fact]
    public void Compatible_orders_share_a_batch_and_respect_max_size()
    {
        var small = Enumerable.Range(1, 13)
            .Select(i => Order($"o{i:00}", 1, Now.AddMinutes(i), new RouteNode("A-01", 1, 0)))
            .ToList();
        var plans = BatchOptimizer.Plan(small);
        Assert.Equal(2, plans.Count);
        Assert.Equal(12, plans[0].OrderIds.Count);
        Assert.Single(plans[1].OrderIds);
        Assert.True(plans[0].VolumeLiters <= BatchOptimizer.DefaultToteLiters);
    }

    [Fact]
    public void Prefers_nearby_orders_in_the_same_ship_window()
    {
        var seed = Order("seed", 2, Now, new RouteNode("A", 1, 0));
        var near = Order("near", 2, Now.AddHours(1), new RouteNode("A", 1, 0));
        var far = Order("far", 2, Now.AddHours(1), new RouteNode("Z", 40, 40));
        var plan = Assert.Single(BatchOptimizer.Plan([seed, far, near]));
        Assert.Equal(["seed", "near", "far"], plan.OrderIds);
        Assert.Equal(["A", "Z"], plan.OptimizedRoute.Select(n => n.Code));
        Assert.True(plan.OptimizedLength <= plan.BaselineLength);
    }

    [Fact]
    public void Later_wave_does_not_block_a_sooner_nearby_order()
    {
        var seed = Order("seed", 2, Now, new RouteNode("A", 1, 0));
        var laterSameCell = Order("later", 2, Now.AddHours(5), new RouteNode("A", 1, 0));
        var soonNearby = Order("soon", 2, Now.AddHours(1), new RouteNode("B", 4, 0));
        var first = BatchOptimizer.Plan([seed, laterSameCell, soonNearby])[0];
        Assert.Equal("seed", first.OrderIds[0]);
        Assert.Equal("soon", first.OrderIds[1]);
    }

    [Fact]
    public void Route_order_and_length_match_planner_for_combined_stops()
    {
        var orders = new[]
        {
            Order("x", 1, Now, new RouteNode("X-01-1", 30, 0)),
            Order("y", 1, Now.AddMinutes(5), new RouteNode("Y-01-1", 1, 10)),
            Order("z", 1, Now.AddMinutes(10), new RouteNode("Z-01-1", 29, 10)),
        };
        var plan = Assert.Single(BatchOptimizer.Plan(orders));
        var stops = BatchOptimizer.DistinctStops(orders);
        var optimized = RoutePlanner.Optimized(stops, s => s);
        var baseline = RoutePlanner.Baseline(stops, s => s);
        Assert.Equal(optimized.Select(s => s.Code), plan.OptimizedRoute.Select(s => s.Code));
        Assert.Equal(baseline.Select(s => s.Code), plan.BaselineRoute.Select(s => s.Code));
        Assert.Equal(RoutePlanner.Length(optimized), plan.OptimizedLength);
        Assert.Equal(RoutePlanner.Length(baseline), plan.BaselineLength);
        Assert.NotEqual(plan.OptimizedRoute.Select(s => s.Code), plan.BaselineRoute.Select(s => s.Code));
        Assert.True(plan.OptimizedLength <= plan.BaselineLength);
    }
}
