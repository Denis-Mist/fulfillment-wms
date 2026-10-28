using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Inbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class InboundHandlersTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext Picker = new("usr_p", "org_fc", Role.FcPicker, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext Seller = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Other = new("usr_o", "org_o", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);

    private sealed class NoDiscrepancy : IReceivingInspectorFactory
    {
        public Domain.Entities.IReceivingInspector Create(DateTimeOffset now) => new Inspector();
        private sealed class Inspector : Domain.Entities.IReceivingInspector
        {
            public ReceivingResult Inspect(InboundLine line) => new(line.ExpectedQty, 0);
        }
    }

    public InboundHandlersTests()
    {
        var n = 0;
        _ids.NewSuffix().Returns(_ => $"x{++n}");
        _clock.UtcNow.Returns(Now);
        _db.Organizations.AddRange(
            new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now),
            new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now),
            new Organization("org_o", OrganizationType.Seller, "Другой", "3", "М", OrganizationStatus.Active, Now));
        _db.Users.Add(new User("usr_fc", "m@x.ru", "h", "Мария Менеджер", null, false));
        _db.Warehouses.Add(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true));
        _db.Contracts.Add(new Contract("ctr_1", "org_fc", "org_s", ["wh_1"], Now));
        _db.Zones.AddRange(new Zone("z_rcv", "wh_1", "RCV", "Приёмка", ZoneType.Receiving), new Zone("z_str", "wh_1", "STR", "Хранение", ZoneType.Storage));
        _db.Locations.AddRange(new StorageLocation("l_rcv", "wh_1", "z_rcv", "R-01-1", 0, 0, 1, 1000),
            new StorageLocation("l_str", "wh_1", "z_str", "A-01-1", 4, 3, 1, 100));
        _db.Products.AddRange(new Product("prd_1", "org_s", "AAA-1", "Банан", "Еда", "12345678", null, 10, 100, 1, false, false),
            new Product("prd_2", "org_s", "BBB-2", "Арбуз", "Еда", "87654321", null, 10, 100, 1, false, false));
        _db.Stock.Add(new StockItem("stk_1", "prd_1", "org_s", "wh_1", "l_str", 10, 0));
        _db.InboundShipments.AddRange(
            Ship("inb_1", "ПС-00001", InboundStatus.InTransit, Now.AddDays(1)),
            Ship("inb_2", "ПС-00002", InboundStatus.Placed, Now.AddDays(-3)),
            Ship("inb_3", "ПС-00003", InboundStatus.Draft, Now.AddDays(2)));
        _db.SaveChanges();
    }

    private static InboundShipment Ship(string id, string number, InboundStatus status, DateTimeOffset expected) =>
        new(id, number, status, "org_s", "wh_1", Now.AddDays(-5), expected, PackageType.Boxes, 1, null, null, null,
            [new InboundLine($"{id}_ln1", "prd_1", 20), new InboundLine($"{id}_ln2", "prd_2", 5)], []);

    public void Dispose() => _db.Dispose();

    private InboundScope Scope => new(_db, new WarehouseScope(_db));
    private InboundQueryHandler Queries => new(_db, Scope);
    private InboundActionHandler Actions => new(_db, Scope, new NoDiscrepancy(), _clock, _ids);
    private static InboundQuery Q(InboundStatus? status = null) => new(new PageQuery(), status, null, null);

    [Fact]
    public async Task List_is_sorted_by_expected_desc_and_scoped()
    {
        var page = await Queries.ListAsync(Fc, Q(), default);
        Assert.Equal(["inb_3", "inb_1", "inb_2"], page.Items.Select(i => i.Id));
        Assert.Equal(("Селлер", "ФЦ", "Склад", 25), (page.Items[0].SellerName, page.Items[0].FulfillmentName, page.Items[0].WarehouseName, page.Items[0].ExpectedUnits));
        Assert.Equal(0, (await Queries.ListAsync(Other, Q(), default)).Total);
        Assert.Single((await Queries.ListAsync(Seller, Q(InboundStatus.Placed), default)).Items);
    }

    [Fact]
    public async Task Summary_ignores_status_filter()
    {
        var summary = await Queries.SummaryAsync(Fc, Q(InboundStatus.Placed), default);
        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.ByStatus["in_transit"]);
    }

    [Fact]
    public async Task Details_hide_foreign_shipments()
    {
        var details = await Queries.DetailsAsync(Seller, "inb_1", default);
        Assert.Equal(["Банан", "Арбуз"], details.Lines.Select(l => l.Name));
        var ex = await Assert.ThrowsAsync<AppException>(() => Queries.DetailsAsync(Other, "inb_1", default));
        Assert.Equal((404, "Поставка не найдена"), (ex.Status, ex.Title));
    }

    [Theory]
    [InlineData("usr_o", "inb_1", "arrive", 404)]
    [InlineData("usr_fc", "inb_1", "teleport", 400)]
    [InlineData("usr_s", "inb_1", "arrive", 403)]
    [InlineData("usr_p", "inb_1", "arrive", 403)]
    [InlineData("usr_fc", "inb_2", "cancel", 409)]
    public async Task Action_errors_follow_mock_order(string user, string id, string action, int status)
    {
        var ctx = user switch { "usr_o" => Other, "usr_s" => Seller, "usr_p" => Picker, _ => Fc };
        var ex = await Assert.ThrowsAsync<AppException>(() => Actions.HandleAsync(ctx, id, action, default));
        Assert.Equal(status, ex.Status);
    }

    [Fact]
    public async Task Seller_can_cancel_own_shipment()
    {
        var result = await Actions.HandleAsync(Seller, "inb_3", "cancel", default);
        Assert.Equal(InboundStatus.Cancelled, result.Status);
        Assert.Equal("—", result.Events.Single().ActorName);
    }

    [Fact]
    public async Task Full_flow_generates_tasks_and_places_stock()
    {
        foreach (var action in new[] { "arrive", "start_receiving", "complete_receiving" })
            await Actions.HandleAsync(Fc, "inb_1", action, default);
        var tasks = _db.Tasks.ToList();
        Assert.Equal([(TaskType.Receiving, WarehouseTaskStatus.Done), (TaskType.Putaway, WarehouseTaskStatus.New)],
            tasks.OrderBy(t => t.Number).Select(t => (t.Type, t.Status)));
        Assert.All(tasks, t => Assert.Equal(("org_fc", "inb_1"), (t.OrganizationId, t.InboundId)));

        var placed = await Actions.HandleAsync(Fc, "inb_1", "complete_placement", default);
        Assert.Equal(InboundStatus.Placed, placed.Status);
        Assert.Equal("Мария Менеджер", placed.Events[^1].ActorName);
        Assert.Equal("Размещено 2 позиций", placed.Events[^1].Description);
        Assert.All(_db.Tasks, t => Assert.Equal(WarehouseTaskStatus.Done, t.Status));
        Assert.Equal(30, _db.Stock.Single(s => s.Id == "stk_1").Quantity);
        var created = _db.Stock.Single(s => s.ProductId == "prd_2");
        Assert.Equal(("l_str", 5), (created.LocationId, created.Quantity));
    }

    [Fact]
    public async Task Audit_source_exposes_inbound_events()
    {
        await Actions.HandleAsync(Fc, "inb_1", "arrive", default);
        var events = await new InboundAuditSource(_db).GetEventsAsync(Now, default);
        var e = Assert.Single(events);
        Assert.Equal((AuditCategory.Inbound, "Машина прибыла на склад", "org_fc", "ФЦ", "ПС-00001"),
            (e.Category, e.Action, e.OrganizationId, e.OrganizationName, e.Entity));
    }
}
