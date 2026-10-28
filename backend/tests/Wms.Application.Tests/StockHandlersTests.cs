using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Stock;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class StockHandlersTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext Seller = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Other = new("usr_o", "org_o", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);

    public StockHandlersTests()
    {
        _ids.NewSuffix().Returns("a1", "b2", "c3");
        _clock.UtcNow.Returns(Now);
        _db.Organizations.AddRange(
            new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now),
            new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now),
            new Organization("org_o", OrganizationType.Seller, "Другой", "3", "М", OrganizationStatus.Active, Now));
        _db.Users.Add(new User("usr_fc", "m@x.ru", "h", "Мария Менеджер", null, false));
        _db.Warehouses.AddRange(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true),
            new Warehouse("wh_2", "org_fc", "Второй", "W2", "М", "ул.", 100, true));
        _db.Contracts.Add(new Contract("ctr_1", "org_fc", "org_s", ["wh_1"], Now));
        _db.Zones.AddRange(new Zone("z_str", "wh_1", "STR", "Хранение", ZoneType.Storage), new Zone("z_def", "wh_1", "DEF", "Брак", ZoneType.Defect),
            new Zone("z_2", "wh_2", "STR", "Хранение", ZoneType.Storage));
        _db.Locations.AddRange(new StorageLocation("l_a", "wh_1", "z_str", "A-01-1", 0, 0, 1, 100),
            new StorageLocation("l_b", "wh_1", "z_str", "B-01-1", 1, 0, 1, 100),
            new StorageLocation("l_d", "wh_1", "z_def", "D-01-1", 2, 0, 1, 100),
            new StorageLocation("l_x", "wh_2", "z_2", "X-01-1", 0, 0, 1, 100));
        _db.Products.AddRange(new Product("prd_1", "org_s", "AAA-1", "Банан", "Еда", "12345678", null, 10, 100, 0.5, false, false),
            new Product("prd_2", "org_s", "BBB-2", "Арбуз", "Еда", "87654321", null, 10, 100, 2, false, false));
        _db.Stock.AddRange(new StockItem("stk_1", "prd_1", "org_s", "wh_1", "l_b", 10, 4),
            new StockItem("stk_2", "prd_2", "org_s", "wh_1", "l_a", 3, 3),
            new StockItem("stk_3", "prd_1", "org_s", "wh_1", "l_d", 2, 0));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private StockReadModel Read => new(_db, new WarehouseScope(_db));
    private StockQueryHandler Queries => new(_db, Read, new WarehouseScope(_db), new Orders.OrderSales(new Orders.OrderScope(_db, new WarehouseScope(_db))), _clock);
    private StockCommandHandler Commands => new(_db, Read, _clock, _ids);
    private StockOperationQueryHandler Ops => new(_db, new WarehouseScope(_db), _clock);
    private static StockQuery Q(StockStatus? status = null) => new(new PageQuery(), null, null, null, status);

    [Fact]
    public async Task List_is_sorted_by_location_with_statuses()
    {
        var page = await Queries.ListAsync(Fc, Q(), default);
        Assert.Equal(["A-01-1", "B-01-1", "D-01-1"], page.Items.Select(i => i.LocationCode));
        Assert.Equal([StockStatus.Reserved, StockStatus.Available, StockStatus.Defect], page.Items.Select(i => i.Status));
        Assert.Equal((6, 5.0), (page.Items[1].Available, page.Items[1].VolumeLiters));
        Assert.Equal(["stk_2", "stk_1"], (await Queries.ListAsync(Fc, Q(StockStatus.Reserved), default)).Items.Select(i => i.Id));
        Assert.Equal(0, (await Queries.ListAsync(Other, Q(), default)).Total);
    }

    [Fact]
    public async Task Summary_counts_defect_and_empty_locations()
    {
        var fc = await Queries.SummaryAsync(Fc, null, null, default);
        Assert.Equal(new StockSummary(2, 15, 7, 6, 2, 1), fc);
        Assert.Equal(0, (await Queries.SummaryAsync(Seller, null, null, default)).EmptyLocations);
    }

    [Fact]
    public async Task Products_aggregate_per_warehouse_without_sales()
    {
        var page = await Queries.ProductsAsync(Fc, new PageQuery(), lowStock: false, default);
        var banana = page.Items.Single(p => p.ProductId == "prd_1");
        Assert.Equal((6, 4, 2, 0.0, (int?)null), (banana.Available, banana.Reserved, banana.Defect, banana.AvgDailySales, banana.DaysOfCover));
        Assert.Equal("ФЦ", Assert.Single(banana.Warehouses).FulfillmentName);
        Assert.Empty((await Queries.ProductsAsync(Fc, new PageQuery(), lowStock: true, default)).Items);
    }

    [Fact]
    public async Task Products_use_30_day_sales_for_days_of_cover()
    {
        Order Make(string id, OrderStatus status, int daysAgo, int qty) => new(id, id, null, SalesChannel.Wb, status, "org_s", "wh_1",
            Now.AddDays(-daysAgo), Now.AddDays(-daysAgo).AddHours(36), null, "Москва", [new OrderLine("prd_1", qty, 10)]);
        _db.Orders.AddRange(Make("o1", OrderStatus.Shipped, 3, 10), Make("o2", OrderStatus.New, 0, 5),
            Make("o3", OrderStatus.Cancelled, 1, 100), Make("o4", OrderStatus.Shipped, 40, 100));
        await _db.SaveChangesAsync();

        var banana = (await Queries.ProductsAsync(Fc, new PageQuery(), lowStock: false, default)).Items.Single(p => p.ProductId == "prd_1");
        Assert.Equal((0.5, (int?)12), (banana.AvgDailySales, banana.DaysOfCover));
        Assert.Equal(["prd_1"], (await Queries.ProductsAsync(Fc, new PageQuery(), lowStock: true, default)).Items.Select(p => p.ProductId));
        Assert.Equal(0.0, (await Queries.ProductsAsync(Other, new PageQuery(), lowStock: false, default)).Items.Sum(p => p.AvgDailySales));
    }

    [Fact]
    public async Task Locations_require_visible_warehouse()
    {
        Assert.Equal(3, (await Queries.LocationsAsync(Seller, "wh_1", null, default)).Count);
        Assert.Equal(ZoneType.Defect, (await Queries.LocationsAsync(Fc, "wh_1", "d-", default)).Single().ZoneType);
        var ex = await Assert.ThrowsAsync<AppException>(() => Queries.LocationsAsync(Seller, "wh_2", null, default));
        Assert.Equal(404, ex.Status);
    }

    [Theory]
    [InlineData("nope", "l_a", 1.0, 404)]
    [InlineData("stk_1", "l_x", 1.0, 422)]
    [InlineData("stk_1", "l_b", 1.0, 422)]
    [InlineData("stk_1", "l_a", 1.5, 422)]
    [InlineData("stk_1", "l_a", 7.0, 409)]
    public async Task Move_validation_follows_mock(string stockId, string to, double qty, int status)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Commands.MoveAsync(Fc, new MoveInput(stockId, to, qty, null), default));
        Assert.Equal(status, ex.Status);
    }

    [Fact]
    public async Task Move_merges_into_existing_row_and_records_operation()
    {
        _db.Stock.Add(new StockItem("stk_4", "prd_1", "org_s", "wh_1", "l_a", 1, 0));
        _db.SaveChanges();
        var op = await Commands.MoveAsync(Fc, new MoveInput("stk_1", "l_a", 6, "перенос"), default);
        Assert.Equal((OperationType.Move, 6, "B-01-1", "A-01-1", "Мария Менеджер", "OP-20000"),
            (op.Type, op.Quantity, op.FromLocationCode, op.ToLocationCode, op.ActorName, op.Number));
        Assert.Equal(4, _db.Stock.Single(s => s.Id == "stk_1").Quantity);
        Assert.Equal(7, _db.Stock.Single(s => s.Id == "stk_4").Quantity);
    }

    [Fact]
    public async Task Move_of_whole_row_creates_target_and_removes_source()
    {
        await Commands.MoveAsync(Fc, new MoveInput("stk_3", "l_a", 2, null), default);
        Assert.DoesNotContain(_db.Stock, s => s.Id == "stk_3");
        Assert.Contains(_db.Stock, s => s.Id.StartsWith("stk_prd_1_l_a_") && s.Quantity == 2);
    }

    [Fact]
    public async Task Inventory_and_writeoff_update_quantity()
    {
        var inv = await Commands.InventoryAsync(Fc, new InventoryInput("stk_1", 8, null), default);
        Assert.Equal((-2, 10, 8, ErrorReason.Shortage), (inv.Quantity, inv.Expected, inv.Counted, inv.Reason));
        var ex = await Assert.ThrowsAsync<AppException>(() => Commands.InventoryAsync(Fc, new InventoryInput("stk_1", 3, null), default));
        Assert.Equal((409, "Факт меньше резерва"), (ex.Status, ex.Title));

        var off = await Commands.WriteoffAsync(Fc, new WriteoffInput("stk_1", 4, "defect", null), default);
        Assert.Equal((-4, ErrorReason.Defect, "OP-20001"), (off.Quantity, off.Reason, off.Number));
        Assert.Equal(4, _db.Stock.Single(s => s.Id == "stk_1").Quantity);
        Assert.Equal(422, (await Assert.ThrowsAsync<AppException>(() => Commands.WriteoffAsync(Fc, new WriteoffInput("stk_1", 1, "lost", null), default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Commands.WriteoffAsync(Fc, new WriteoffInput("stk_1", 1, "defect", null), default))).Status);

        var summary = await Ops.SummaryAsync(Fc, null, default);
        Assert.Equal((2, 1, 1), (summary.Last30Days, summary.ByType["inventory"], summary.ByType["writeoff"]));
        Assert.Equal(4, summary.Errors.Single(e => e.Reason == ErrorReason.Defect).Quantity);
        Assert.Equal(2, summary.Errors.Single(e => e.Reason == ErrorReason.Shortage).Quantity);
        Assert.Equal(2, (await Ops.ListAsync(Fc, new OperationQuery(new PageQuery(), null, null, "stk_1"), default)).Total);
        Assert.Equal(0, (await Ops.ListAsync(Other, new OperationQuery(new PageQuery(), null, null, null), default)).Total);
    }
}
