using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Analytics;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class KpiHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);

    public KpiHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _db.Organizations.AddRange(
            new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now),
            new Organization("org_s", OrganizationType.Seller, "Селлер А", "2", "М", OrganizationStatus.Active, Now),
            new Organization("org_s2", OrganizationType.Seller, "Селлер Б", "3", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.AddRange(
            new Warehouse("wh_1", "org_fc", "Склад 1", "W1", "М", "ул.", 100, true),
            new Warehouse("wh_2", "org_fc", "Склад 2", "W2", "М", "ул.", 100, true));
        _db.Locations.AddRange(
            new StorageLocation("loc_a", "wh_1", "z1", "A-01", 20, 20, 1, 100),
            new StorageLocation("loc_b", "wh_1", "z1", "B-01", 1, 0, 1, 100),
            new StorageLocation("loc_c", "wh_1", "z1", "C-01", 20, 0, 1, 100));
        _db.Orders.AddRange(
            Order("ord_1", "org_s", "wh_1", Day(18, 10), Day(19, 10), OrderStatus.Shipped),
            Order("ord_2", "org_s2", "wh_2", Day(18, 11), null, OrderStatus.Packed),
            Order("ord_old", "org_s", "wh_1", Day(1, 10), Day(2, 10), OrderStatus.Shipped),
            Order("ord_cancel", "org_s", "wh_1", Day(18, 12), null, OrderStatus.Cancelled));
        var picking = WarehouseTask.Restore("tsk_1", "T-1", TaskType.Picking, WarehouseTaskStatus.Done, TaskPriority.Normal,
            "wh_1", "org_fc", "usr_p", Day(17, 8), Day(17, 9), Day(17, 11), ["ord_1"], null, "optimized",
            [new TaskItem("prd_1", "loc_a", 1), new TaskItem("prd_1", "loc_b", 1), new TaskItem("prd_1", "loc_c", 1)]);
        _db.Tasks.Add(picking);
        _db.StockOperations.Add(new StockOperation("op_1", "OP-1", OperationType.Writeoff, Day(18, 12), "Иван", "wh_1", "Склад 1",
            "prd_1", "SKU", "Товар", "Селлер А", 1, reason: ErrorReason.Defect));
        _db.StaffProfiles.Add(new StaffProfile("usr_p", "wh_1", [], [Skill.Picking], true, 60, 99, Now,
            Enumerable.Repeat(Shift.Day, 7).ToList()));
        _db.SaveChanges();
    }

    private static DateTimeOffset Day(int day, int hour) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    private static Order Order(string id, string sellerId, string warehouseId, DateTimeOffset created, DateTimeOffset? shipped, OrderStatus status) =>
        new(id, id, null, SalesChannel.Wb, status, sellerId, warehouseId, created, created.AddHours(36), shipped, "Москва",
            [new OrderLine("prd_1", 1, 10)]);

    public void Dispose() => _db.Dispose();

    private AnalyticsHandler Handler => new(_db, _db, _clock, new WarehouseScope(_db));

    [Fact]
    public async Task Kpi_uses_real_orders_routes_and_slices()
    {
        var report = await Handler.KpiAsync(Fc, null, null, 7, default);
        Assert.Equal(7, report.Period);
        Assert.Equal(7, report.Series.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.Zero), report.Series[0].Date);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), report.Series[^1].Date);

        var createdDay = report.Series.Single(p => p.Date.Day == 18);
        Assert.Equal(2, createdDay.Orders);
        Assert.Equal(1, createdDay.Errors);
        Assert.Equal(24, report.Series.Single(p => p.Date.Day == 19).ProcessingHours);
        Assert.Equal(2, report.Totals.Orders);
        Assert.Equal(24, report.Totals.AvgProcessingHours);
        Assert.True(report.Totals.RouteReductionPct > 0);
        Assert.True(report.Totals.AvgRouteBaseline >= report.Totals.AvgRouteOptimized);
        Assert.Equal(50, report.Totals.ErrorRate);

        var sellerA = Assert.Single(report.BySeller, s => s.SellerId == "org_s");
        Assert.Equal(1, sellerA.Orders);
        Assert.Equal(24, sellerA.AvgProcessingHours);
        Assert.True(sellerA.ErrorRate > 0);
        Assert.Contains(report.BySeller, s => s.SellerId == "org_s2" && s.Orders == 1);
        Assert.Equal(1, report.ByWarehouse.Single(w => w.WarehouseId == "wh_1").Orders);
        Assert.Equal(1, report.ByWarehouse.Single(w => w.WarehouseId == "wh_2").Orders);
        Assert.Equal(2, report.ByWarehouse.Count);

        var routeDay = report.Series.Single(p => p.Date.Day == 17);
        Assert.True(routeDay.RouteBaseline > routeDay.RouteOptimized);
        Assert.True(routeDay.Utilization > 0);
    }

    [Fact]
    public async Task Kpi_filters_by_seller_and_warehouse()
    {
        var seller = await Handler.KpiAsync(Fc, null, "org_s", 7, default);
        Assert.Equal(1, seller.Totals.Orders);
        Assert.All(seller.BySeller, s => Assert.Equal("org_s", s.SellerId));
        Assert.Equal(0, seller.ByWarehouse.Single(w => w.WarehouseId == "wh_2").Orders);

        var warehouse = await Handler.KpiAsync(Fc, "wh_2", null, 7, default);
        Assert.Equal(1, warehouse.Totals.Orders);
        Assert.Equal("wh_2", Assert.Single(warehouse.ByWarehouse).WarehouseId);
        Assert.Equal(0, warehouse.Totals.RouteReductionPct);
    }

    [Fact]
    public async Task Kpi_prefers_stored_route_metrics()
    {
        var extra = new WarehouseTask("tsk_stored", "T-2", TaskType.Picking, "wh_1", "org_fc", Day(16, 8),
            [new TaskItem("prd_1", "loc_a", 1)]);
        extra.Complete(Day(16, 9));
        extra.RecordMetrics(80, 100, 5, 1, 60);
        _db.Tasks.Add(extra);
        _db.SaveChanges();

        var report = await Handler.KpiAsync(Fc, "wh_1", null, 7, default);
        var storedDay = report.Series.Single(p => p.Date.Day == 16);
        Assert.Equal(100, storedDay.RouteBaseline);
        Assert.Equal(80, storedDay.RouteOptimized);
    }
}
