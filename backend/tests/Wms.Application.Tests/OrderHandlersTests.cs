using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Orders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class OrderHandlersTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext Seller = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Other = new("usr_o", "org_o", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);

    public OrderHandlersTests()
    {
        _clock.UtcNow.Returns(Now);
        _db.Organizations.AddRange(
            new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now),
            new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now),
            new Organization("org_o", OrganizationType.Seller, "Другой", "3", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.AddRange(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true),
            new Warehouse("wh_x", "org_x", "Чужой", "X1", "М", "ул.", 100, true));
        _db.Products.Add(new Product("prd_1", "org_s", "AAA-1", "Банан", "Еда", "12345678", null, 10, 100, 0.5, false, false));
        _db.Orders.AddRange(
            Make("ord_1", OrderStatus.Shipped, SalesChannel.Wb, "org_s", "wh_1", -50, shippedHours: -40, ext: "111"),
            Make("ord_2", OrderStatus.Picking, SalesChannel.Ozon, "org_s", "wh_1", -40, city: "Казань"),
            Make("ord_3", OrderStatus.New, SalesChannel.Own, "org_s", "wh_1", -2),
            Make("ord_4", OrderStatus.Cancelled, SalesChannel.Wb, "org_o", "wh_x", -1));
        _db.SaveChanges();
    }

    private static Order Make(string id, OrderStatus status, SalesChannel channel, string seller, string wh, int createdHours,
        int? shippedHours = null, string city = "Москва", string? ext = null) =>
        new(id, $"ЗК-{id[4..]}", ext, channel, status, seller, wh, Now.AddHours(createdHours), Now.AddHours(createdHours + 36),
            shippedHours is { } s ? Now.AddHours(s) : null, city, [new OrderLine("prd_1", 2, 100), new OrderLine("prd_1", 1, 50)]);

    public void Dispose() => _db.Dispose();

    private OrderScope Scope => new(_db, new WarehouseScope(_db));
    private OrderQueryHandler Handler => new(_db, Scope, _clock);
    private static OrderQuery Q(string? status = null, SalesChannel? channel = null, string? search = null, string? sort = null) =>
        new(new PageQuery(Search: search, SortField: sort), status, channel, null, null);

    [Fact]
    public async Task List_defaults_to_newest_and_maps_totals()
    {
        var page = await Handler.ListAsync(Fc, Q(), default);
        Assert.Equal(["ord_3", "ord_2", "ord_1"], page.Items.Select(o => o.Id));
        var first = page.Items[0];
        Assert.Equal((2, 3, 250.0, "Селлер", "Склад", false), (first.LinesCount, first.UnitsCount, first.Amount, first.SellerName, first.WarehouseName, first.IsOverdue));
        Assert.True(page.Items[1].IsOverdue);
    }

    [Fact]
    public async Task Filters_status_overdue_channel_search_and_ignores_unknown()
    {
        Assert.Equal(["ord_2"], (await Handler.ListAsync(Fc, Q("overdue"), default)).Items.Select(o => o.Id));
        Assert.Equal(["ord_3"], (await Handler.ListAsync(Fc, Q("new"), default)).Items.Select(o => o.Id));
        Assert.Equal(3, (await Handler.ListAsync(Fc, Q("bogus"), default)).Total);
        Assert.Equal(["ord_2"], (await Handler.ListAsync(Fc, Q(channel: SalesChannel.Ozon), default)).Items.Select(o => o.Id));
        Assert.Equal(["ord_2"], (await Handler.ListAsync(Fc, Q(search: "казан"), default)).Items.Select(o => o.Id));
        Assert.Equal(["ord_1"], (await Handler.ListAsync(Fc, Q(search: "111"), default)).Items.Select(o => o.Id));
    }

    [Fact]
    public async Task Summary_ignores_status_and_counts_overdue()
    {
        var summary = await Handler.SummaryAsync(Seller, Q("shipped"), default);
        Assert.Equal((3, 1), (summary.Total, summary.Overdue));
        Assert.Equal(1, summary.ByStatus["picking"]);
        Assert.Equal(1, (await Handler.SummaryAsync(Other, Q(), default)).Total);
    }

    [Fact]
    public async Task Details_respect_scope()
    {
        var order = await Handler.DetailsAsync(Seller, "ord_1", default);
        Assert.Equal(("AAA-1", "Банан", 2), (order.Lines[0].Sku, order.Lines[0].Name, order.Lines.Count));
        var ex = await Assert.ThrowsAsync<AppException>(() => Handler.DetailsAsync(Fc, "ord_4", default));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task Sales_search_notifications_and_audit()
    {
        var sales = await new OrderSales(Scope).ByProductAsync(Seller, Now, default);
        Assert.Equal(9, sales["prd_1"]);
        Assert.Empty(await new OrderSales(Scope).ByProductAsync(Other, Now, default));

        var found = await new OrderSearchProvider(_db, Scope).SearchAsync(Fc, "зк-", default);
        Assert.Equal(3, found.Count);
        Assert.StartsWith("В отборе · Селлер", found.Single(r => r.Id == "ord_2").Subtitle);

        var note = Assert.Single(await new OverdueOrdersNotificationSource(Scope).GetAsync(Fc, Now, default));
        Assert.Equal("1 заказов не отгружены в срок", note.Text);
        Assert.Empty(await new OverdueOrdersNotificationSource(Scope).GetAsync(Seller, Now, default));

        var audit = Assert.Single(await new ShippedOrdersAuditSource(_db).GetEventsAsync(Now, default));
        Assert.Equal(("aud_ship_ord_1", "org_fc", "2 строк, Москва"), (audit.Id, audit.OrganizationId, audit.Details));
    }
}
