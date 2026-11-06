using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Analytics;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class SlottingHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);

    public SlottingHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _db.Organizations.Add(new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.Add(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true));
        _db.Locations.AddRange(
            new StorageLocation("loc_far", "wh_1", "z1", "Z-99-1", 20, 20, 1, 200),
            new StorageLocation("loc_near", "wh_1", "z1", "A-01-1", 2, 2, 1, 200),
            new StorageLocation("loc_tiny", "wh_1", "z1", "A-00-1", 1, 1, 1, 5));
        _db.Products.AddRange(
            new Product("prd_hot", "org_s", "HOT-1", "Хит продаж", "Еда", "12345678", null, 10, 200, 2, false, false),
            new Product("prd_c1", "org_s", "C-1", "Холодный 1", "Еда", "12345679", null, 10, 100, 0.5, false, false),
            new Product("prd_c2", "org_s", "C-2", "Холодный 2", "Еда", "12345670", null, 10, 100, 0.5, false, false));
        _db.Stock.Add(new StockItem("stk_hot", "prd_hot", "org_s", "wh_1", "loc_far", 10, 2));
        foreach (var (id, n) in new[] { ("prd_hot", 16), ("prd_c1", 2), ("prd_c2", 2) })
            for (var i = 0; i < n; i++)
                _db.Orders.Add(Make($"ord_{id}_{i}", id, Now.AddDays(-2)));
        _db.SaveChanges();
    }

    private static Order Make(string id, string productId, DateTimeOffset created) =>
        new(id, id, null, SalesChannel.Wb, OrderStatus.Shipped, "org_s", "wh_1", created, created.AddHours(12), created.AddHours(6),
            "Москва", [new OrderLine(productId, 1, 10)]);

    public void Dispose() => _db.Dispose();

    private AnalyticsHandler Handler => new(_db, _db, _clock, new WarehouseScope(_db));

    [Fact]
    public async Task Slotting_uses_capacity_and_real_picks_not_tiny_cell()
    {
        var result = await Handler.SlottingAsync(Fc, "wh_1", SlottingAlgorithm.Abc, default);
        Assert.Equal("2.0.0", result.Meta.Version);
        var move = Assert.Single(result.Moves);
        Assert.Equal("stk_hot", move.Id[move.Id.LastIndexOf("stk_", StringComparison.Ordinal)..]);
        Assert.Equal("Z-99-1", move.FromCell);
        Assert.Equal("A-01-1", move.ToCell);
        Assert.NotEqual("A-00-1", move.ToCell);
        Assert.True(move.Gain > 0);
        Assert.Equal(16, move.Picks30d);
        Assert.Equal(2, move.VolumeLiters);
        Assert.Equal(200, move.CapacityLiters);
    }

    [Fact]
    public async Task Apply_moves_only_free_stock_and_keeps_reserved()
    {
        var planned = await Handler.SlottingAsync(Fc, "wh_1", SlottingAlgorithm.Abc, default);
        var id = Assert.Single(planned.Moves).Id;
        var applied = await Handler.ApplyAsync(Fc, new ApplyInput([id]), default);
        Assert.Equal(1, applied.Applied);

        var remaining = _db.Stock.ToList();
        Assert.Equal(10, remaining.Sum(s => s.Quantity));
        Assert.Equal(2, remaining.Sum(s => s.Reserved));
        var atNear = remaining.Single(s => s.LocationId == "loc_near");
        Assert.Equal(8, atNear.Quantity);
        Assert.Equal(0, atNear.Reserved);
        var atFar = remaining.Single(s => s.LocationId == "loc_far");
        Assert.Equal(2, atFar.Quantity);
        Assert.Equal(2, atFar.Reserved);
    }
}
