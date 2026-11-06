using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Analytics;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class ForecastHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);

    public ForecastHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _db.Organizations.Add(new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.AddRange(
            new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true),
            new Warehouse("wh_2", "org_fc", "Склад 2", "W2", "М", "ул.", 100, true));
        _db.Users.Add(new User("usr_fc", "director@demo.ru", "hash", "Директор", null, false));
        var peak = Start.AddDays(-10).AddHours(9);
        _db.Orders.AddRange(
            Enumerable.Range(1, 5).Select(i => Make($"ord_a{i}", "wh_1", peak, OrderStatus.Shipped)));
        _db.Orders.AddRange(
            Enumerable.Range(1, 2).Select(i => Make($"ord_b{i}", "wh_2", peak, OrderStatus.Packed)));
        _db.Orders.Add(Make("ord_c", "wh_1", peak, OrderStatus.Cancelled));
        _db.Orders.Add(Make("ord_today", "wh_1", Start.AddHours(3), OrderStatus.New));
        _db.Orders.Add(Make("ord_old", "wh_1", Start.AddDays(-60), OrderStatus.Shipped));
        _db.Tasks.Add(new WarehouseTask("tsk_p1", "P-1", TaskType.Picking, "wh_1", "org_fc", peak,
            [new TaskItem("prd_1", "loc_1", 2), new TaskItem("prd_1", "loc_1", 1)]));
        _db.SaveChanges();
    }

    private static Order Make(string id, string warehouseId, DateTimeOffset created, OrderStatus status) =>
        new(id, $"ЗК-{id}", null, SalesChannel.Wb, status, "org_s", warehouseId, created, created.AddHours(36),
            status == OrderStatus.Shipped ? created.AddHours(12) : null, "Москва",
            [new OrderLine("prd_1", 2, 100), new OrderLine("prd_1", 1, 50)]);

    public void Dispose() => _db.Dispose();

    private AnalyticsHandler Handler => new(_db, _db, _clock, new WarehouseScope(_db));

    [Fact]
    public async Task Forecast_counts_real_orders_per_utc_day_and_ignores_cancelled_today_and_old()
    {
        var all = await Handler.ForecastAsync(Fc, null, 7, ForecastAlgorithm.HoltWinters, default);
        Assert.Equal(56 + 7, all.Series.Count);
        Assert.Equal("2.0.0", all.Meta.Version);
        Assert.StartsWith("orders-7-picks-2-snap-2026-10-01", all.Meta.DataVersion);
        Assert.Equal(7, PeakActual(all, -10));
        Assert.Equal(0, PeakActual(all, -11));
        Assert.DoesNotContain(all.Series, p => p.Date == Start && p.Actual is not null);
        Assert.All(all.Staffing.Where(s => s.Orders > 0), s => Assert.Equal(Forecasting.Round(s.Orders * 3), s.Units));

        var one = await Handler.ForecastAsync(Fc, "wh_1", 7, ForecastAlgorithm.MovingAverage, default);
        Assert.Equal(5, PeakActual(one, -10));
        Assert.StartsWith("orders-5-picks-2-snap-", one.Meta.DataVersion);
        Assert.Equal("moving_average", one.Meta.Algorithm);
        Assert.True(PeakActual(one, -10) < PeakActual(all, -10));
    }

    [Fact]
    public async Task Forecast_is_not_the_synthetic_history()
    {
        var result = await Handler.ForecastAsync(Fc, "wh_1", 7, null, default);
        var seed = SeededRandom.Hash("wh_1");
        var synthetic = Forecasting.History(seed, 60, Start);
        var actuals = result.Series.Take(56).Select(p => p.Actual ?? 0).ToList();
        Assert.NotEqual(synthetic.Select(p => p.Value).ToList(), actuals);
        Assert.Equal(5, actuals.Sum());
    }

    [Fact]
    public async Task Approve_persists_plan_returned_on_next_forecast()
    {
        var plan = await Handler.ApproveAsync(Fc, new ApproveInput("wh_1", "moving_average"), default);
        Assert.Equal(ForecastAlgorithm.MovingAverage, plan.Algorithm);
        var forecast = await Handler.ForecastAsync(Fc, "wh_1", 7, null, default);
        Assert.Equal(ForecastAlgorithm.MovingAverage, forecast.Plan?.Algorithm);
    }

    [Fact]
    public async Task Forecast_unknown_warehouse_is_not_found()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Handler.ForecastAsync(Fc, "wh_missing", 7, null, default));
        Assert.Equal(404, ex.Status);
    }

    private static int PeakActual(ForecastResult result, int daysFromToday) =>
        result.Series.Single(p => p.Date == Start.AddDays(daysFromToday) && p.Actual is not null).Actual!.Value;
}
