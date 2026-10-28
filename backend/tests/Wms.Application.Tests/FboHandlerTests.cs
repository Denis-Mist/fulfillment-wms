using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Fbo;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class FboHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Seller = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Other = new("usr_o", "org_o", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Fc = new("usr_m", "org_fc", Role.FcManager, OrganizationStatus.Active, OrganizationType.Fulfillment);

    public FboHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.NewSuffix().Returns("x1");
        var seller = new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now);
        _db.Organizations.AddRange(new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now), seller,
            new Organization("org_o", OrganizationType.Seller, "Другой", "3", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.Add(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true));
        _db.Contracts.Add(new Contract("ctr_1", "org_fc", "org_s", ["wh_1"], Now));
        var user = new User("usr_s", "s@x.ru", "h", "Сергей", null, false);
        user.JoinOrganization(seller, Role.SellerOwner);
        _db.Users.Add(user);
        _db.Products.AddRange(new Product("prd_1", "org_s", "BBB-1", "Банан", "Еда", "111", null, 10, 100, 0.5, false, false),
            new Product("prd_2", "org_s", "AAA-2", "Яблоко", "Еда", "222", null, 10, 100, 0.5, false, false));
        _db.Stock.AddRange(new StockItem("stk_1", "prd_1", "org_s", "wh_1", "l_a", 10, 2), new StockItem("stk_2", "prd_1", "org_s", "wh_1", "l_b", 5, 0),
            new StockItem("stk_3", "prd_2", "org_s", "wh_1", "l_c", 3, 3));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private FboHandler Handler => new(_db, _db, new WarehouseScope(_db), _ids, _clock);

    private static FboInput Input(int quantity, string destination = "WB Казань") =>
        new(FboMarketplace.Wb, destination, "wh_1", Now.AddDays(2), [new FboLineInput("prd_1", quantity), new FboLineInput("prd_2", 0)]);

    [Fact]
    public async Task Stock_options_sum_free_stock_for_seller_only()
    {
        var options = await Handler.StockOptionsAsync(Seller, default);
        var single = Assert.Single(options);
        Assert.Equal(13, single.Available);
        Assert.Equal("Склад", single.WarehouseName);
        Assert.Empty(await Handler.StockOptionsAsync(Fc, default));
    }

    [Fact]
    public async Task Create_validates_and_flows_through_statuses()
    {
        Assert.Equal("Выберите склад маркетплейса", (await Assert.ThrowsAsync<AppException>(() => Handler.CreateAsync(Seller, Input(1, "Ozon Тверь"), default))).Title);
        var lack = await Assert.ThrowsAsync<AppException>(() => Handler.CreateAsync(Seller, Input(14), default));
        Assert.Equal("BBB-1: доступно 13 шт.", lack.Detail);
        Assert.Equal("Выберите склад фулфилмента", (await Assert.ThrowsAsync<AppException>(() => Handler.CreateAsync(Other, Input(1), default))).Title);

        var created = await Handler.CreateAsync(Seller, Input(13), default);
        Assert.Equal("fbo_ux1", created.Id);
        Assert.Equal("FBO-3101", created.Number);
        Assert.Equal(FboStatus.Draft, created.Status);
        Assert.Equal("Сергей", created.History[0].By);
        Assert.Single(created.Lines);

        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Handler.SubmitAsync(Other, created.Id, default))).Status);
        var submitted = await Handler.SubmitAsync(Seller, created.Id, default);
        Assert.StartsWith("WB-GI-", submitted.SupplyId);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Handler.SubmitAsync(Seller, created.Id, default))).Status);

        var summary = await Handler.SummaryAsync(Fc, null, default);
        Assert.Equal(0, summary.DueToday); // плановая дата послезавтра
        Assert.Equal(13, summary.UnitsInWork);

        var assembling = await Handler.AdvanceAsync(Fc, created.Id, FboStatus.Assembling, default);
        Assert.Equal(0, assembling.Packed);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Handler.CancelAsync(Seller, created.Id, default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => Handler.AdvanceAsync(Fc, created.Id, FboStatus.Shipped, default))).Status);
        Assert.Equal(13, (await Handler.AdvanceAsync(Fc, created.Id, FboStatus.Ready, default)).Packed);

        var page = await Handler.ListAsync(Seller, new FboQuery(new PageQuery(Search: "казань"), FboStatus.Ready, null, null, null), default);
        Assert.Equal(1, page.Total);
        Assert.Equal(0, (await Handler.ListAsync(Other, new FboQuery(new PageQuery(), null, null, null, null), default)).Total);
    }
}
