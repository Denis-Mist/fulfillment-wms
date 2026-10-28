using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Returns;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class ReturnsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Seller = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Other = new("usr_o", "org_o", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Fc = new("usr_m", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);

    public ReturnsHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _db.Organizations.AddRange(new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now),
            new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.Add(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true));
        _db.Products.Add(new Product("prd_1", "org_s", "AAA-1", "Банан", "Еда", "111", null, 10, 100, 0.5, false, false));
        _db.Stock.AddRange(new StockItem("stk_1", "prd_1", "org_s", "wh_1", "l_a", 10, 0), new StockItem("stk_2", "prd_1", "org_s", "wh_1", "l_b", 5, 0));
        _db.Returns.AddRange(
            new CustomerReturn("ret_1", "ВЗ-1", "ord_x", ReturnReason.Size, ReturnStatus.Expected, "org_s", "wh_1", Now.AddDays(-1), null, null, null, null, 0,
                [new ReturnLine("prd_1", 2)]),
            new CustomerReturn("ret_2", "ВЗ-2", "ord_y", ReturnReason.Defect, ReturnStatus.Inspected, "org_s", "wh_1", Now.AddDays(-2), Now, Now, "x", null, 1,
                [new ReturnLine("prd_1", 3, ReturnGrade.Defect)]));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private ReturnsHandler Handler => new(_db, _db, new WarehouseScope(_db), _clock);

    [Fact]
    public async Task Summary_and_list_follow_mock()
    {
        var summary = await Handler.SummaryAsync(Seller, null, null, default);
        Assert.Equal(2, summary.Total);
        Assert.Equal(0, summary.RestockRate);
        Assert.Equal(3, summary.DefectUnits);
        Assert.Equal(1, summary.ByStatus["expected"]);
        var page = await Handler.ListAsync(Seller, new ReturnQuery(new PageQuery(), null, ReturnReason.Size, null, null), default);
        var item = Assert.Single(page.Items);
        Assert.Equal("—", item.OrderNumber);
        Assert.Equal(SalesChannel.Wb, item.Channel);
        Assert.Equal(0, (await Handler.SummaryAsync(Other, null, null, default)).Total);
        await Assert.ThrowsAsync<AppException>(() => Handler.DetailsAsync(Other, "ret_1", default));
    }

    [Fact]
    public async Task Close_restocks_first_stock_item()
    {
        await Handler.ReceiveAsync(Fc, "ret_1", default);
        var missing = await Assert.ThrowsAsync<AppException>(() => Handler.InspectAsync(Fc, "ret_1", new InspectInput(null, null), default));
        Assert.Equal("Оцените все позиции", missing.Title);
        var inspected = await Handler.InspectAsync(Fc, "ret_1", new InspectInput(new Dictionary<string, ReturnGrade> { ["prd_1"] = ReturnGrade.Resale }, null), default);
        Assert.Equal(ReturnResolution.Restocked, inspected.Resolution);
        Assert.Equal("AAA-1", inspected.Lines[0].Sku);
        var closed = await Handler.CloseAsync(Fc, "ret_1", default);
        Assert.Equal(ReturnStatus.Closed, closed.Status);
        Assert.Equal(12, _db.Stock.Single(s => s.Id == "stk_1").Quantity);
        Assert.Equal(5, _db.Stock.Single(s => s.Id == "stk_2").Quantity);
        await Assert.ThrowsAsync<AppException>(() => Handler.CloseAsync(Fc, "ret_1", default));
    }
}
