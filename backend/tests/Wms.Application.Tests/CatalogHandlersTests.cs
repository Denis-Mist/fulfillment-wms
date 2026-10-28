using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Catalog;
using Wms.Application.Common;
using Wms.Application.Warehouses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class CatalogHandlersTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly RequestContext Fc = new("usr_fc", "org_fc", Role.FcOwner, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext Seller = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
    private static readonly RequestContext Other = new("usr_o", "org_o", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);

    public CatalogHandlersTests()
    {
        _ids.NewSuffix().Returns("a", "b", "c", "d");
        _db.Organizations.AddRange(
            new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now),
            new Organization("org_s", OrganizationType.Seller, "Селлер", "2", "М", OrganizationStatus.Active, Now),
            new Organization("org_o", OrganizationType.Seller, "Другой", "3", "М", OrganizationStatus.Active, Now));
        _db.Warehouses.Add(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true));
        _db.Contracts.Add(new Contract("ctr_1", "org_fc", "org_s", ["wh_1"], Now));
        _db.Zones.AddRange(new Zone("wh_1_z_STG", "wh_1", "STG", "Хранение", ZoneType.Storage),
            new Zone("wh_1_z_RCV", "wh_1", "RCV", "Приёмка", ZoneType.Receiving));
        _db.Locations.AddRange(new StorageLocation("wh_1_l_A-01-1", "wh_1", "wh_1_z_STG", "A-01-1", 4, 3, 1, 100),
            new StorageLocation("wh_1_l_A-01-2", "wh_1", "wh_1_z_STG", "A-01-2", 4, 3, 2, 100));
        _db.Products.AddRange(
            new Product("prd_1", "org_s", "AAA-1", "Банан", "Еда", "12345678", null, 10, 100, 2, false, true),
            new Product("prd_2", "org_s", "BBB-2", "Арбуз", "Еда", "87654321", null, 10, 100, 1, true, false),
            new Product("prd_3", "org_o", "CCC-3", "Чужой", "Дом", "11112222", null, 10, 100, 1, false, false));
        _db.Stock.Add(new StockItem("stk_1", "prd_1", "org_s", "wh_1", "wh_1_l_A-01-1", 10, 2));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private CatalogReader Reader => new(_db, new WarehouseScope(_db));
    private ProductCommandsHandler Commands => new(_db, Reader, _ids);
    private static ProductQuery Q(string? flag = null, string? search = null) => new(new PageQuery(null, null, search), null, null, flag);

    [Fact]
    public async Task List_is_scoped_sorted_by_name_and_counts_stock()
    {
        var page = await new ProductListHandler(Reader).HandleAsync(Fc, Q(), default);
        Assert.Equal(["Арбуз", "Банан"], page.Items.Select(i => i.Name));
        Assert.Equal(10, page.Items[1].StockQty);
        Assert.Equal("Селлер", page.Items[1].SellerName);
        Assert.Single((await new ProductListHandler(Reader).HandleAsync(Fc, Q("no_stock"), default)).Items);
    }

    [Fact]
    public async Task Facets_count_active_products_and_archived()
    {
        await Commands.ActAsync(Seller, "prd_2", "archive", default);
        var f = await new ProductFacetsHandler(Reader).HandleAsync(Seller, Q(), default);
        Assert.Equal((1, 1, 0, 0, 1), (f.Total, f.Marking, f.Expiry, f.NoStock, f.Archived));
        Assert.Equal([new CategoryFacet("Еда", 1)], f.Categories);
    }

    [Fact]
    public async Task Archive_with_stock_and_unknown_action_fail_like_mock()
    {
        var conflict = await Assert.ThrowsAsync<AppException>(() => Commands.ActAsync(Seller, "prd_1", "archive", default));
        Assert.Equal((409, "Нельзя архивировать товар с остатком"), (conflict.Status, conflict.Title));
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Commands.ActAsync(Seller, "prd_1", "explode", default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Commands.ActAsync(Other, "prd_1", "restore", default))).Status);
    }

    [Fact]
    public async Task Create_rejects_duplicates_with_422()
    {
        var draft = new ProductDraft("aaa-1", "Новый товар", null, "99998888", null, 5, 5, 1, null, null);
        var e = await Assert.ThrowsAsync<AppException>(() => Commands.CreateAsync(Seller, draft, default));
        Assert.Equal((422, "Товар с таким артикулом или штрихкодом уже есть: AAA-1"), (e.Status, e.Title));
        var created = await Commands.CreateAsync(Seller, draft with { Sku = "NEW-1" }, default);
        Assert.Equal(("prd_a", "Без категории", 0), (created.Id, created.Category, created.StockQty));
    }

    [Fact]
    public async Task Import_creates_updates_and_reports_rows()
    {
        var result = await Commands.ImportAsync(Seller, new ImportInput([
            new ProductDraft("aaa-1", "Банан новый", "Фрукты", "12345678", null, 20, 100, 2, null, true),
            new ProductDraft("NEW-2", "Новинка", null, "55556666", null, 1, 1, 1, null, null),
            new ProductDraft("NEW-3", "Плохой", null, "1", null, 1, 1, 1, null, null),
        ]), default);
        Assert.Equal((1, 1), (result.Created, result.Updated));
        Assert.Equal([new ImportError(4, "Штрихкод должен содержать 8 или 13 цифр")], result.Errors);
        Assert.Equal("Банан новый", _db.Products.Single(p => p.Id == "prd_1").Name);
    }

    [Fact]
    public async Task Search_provider_finds_visible_products_only()
    {
        var results = await new ProductSearchProvider(Reader).SearchAsync(Seller, "ccc", default);
        Assert.Empty(results);
        var mine = await new ProductSearchProvider(Reader).SearchAsync(Seller, "8765", default);
        Assert.Equal("Арбуз", Assert.Single(mine).Title);
    }

    [Fact]
    public async Task Putaway_check_uses_policy_and_validates_input()
    {
        var handler = new PutawayCheckHandler(_db, new WarehouseScope(_db));
        var result = await handler.HandleAsync(Fc, "wh_1", new PutawayCheckInput("wh_1_l_A-01-1", "prd_3", 1), default);
        Assert.False(result.Allowed);
        Assert.False(result.Checks[0].Passed);
        Assert.Equal(79, result.FreeLitersAfter);
        var e = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(Fc, "wh_1", new PutawayCheckInput("wh_1_l_A-01-1", "prd_1", 0), default));
        Assert.Equal("Количество должно быть больше нуля", e.Title);
    }

    [Fact]
    public async Task Zone_and_location_creation_conflicts()
    {
        var zones = new CreateZoneHandler(_db, new WarehouseScope(_db));
        Assert.Equal(409, (await Assert.ThrowsAsync<AppException>(() => zones.HandleAsync(Fc, "wh_1", new ZoneInput("stg", "X", "storage"), default))).Status);
        var created = await zones.HandleAsync(Fc, "wh_1", new ZoneInput("pck", "Отбор", "picking"), default);
        Assert.Equal("wh_1_z_PCK", created.Id);
        var locations = new CreateLocationHandler(_db, new WarehouseScope(_db));
        var e = await Assert.ThrowsAsync<AppException>(() => locations.HandleAsync(Fc, "wh_1", new LocationInput("wh_1_z_STG", "a", 1, 1, 50), default));
        Assert.Equal((409, "Ячейка A-01-1 уже существует"), (e.Status, e.Title));
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => zones.HandleAsync(Other, "wh_x", new ZoneInput("AB", "", "storage"), default))).Status);
    }
}
