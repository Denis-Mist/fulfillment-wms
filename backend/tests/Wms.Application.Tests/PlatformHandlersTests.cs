using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Admin;
using Wms.Application.Auth;
using Wms.Application.Clients;
using Wms.Application.Common;
using Wms.Application.Partners;
using Wms.Application.Settings;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class MockParityTests
{
    [Fact]
    public void Mulberry32_matches_mock_createRandom()
    {
        var rng = new Mulberry32(12345);
        Assert.Equal(0.9797282677609473, rng.Next());
        Assert.Equal(4, rng.Int(3, 8));
        Assert.Equal(4.563154066144489, rng.Float(4.2, 4.95));
        Assert.False(rng.Bool(0.6));
        Assert.Equal(24, rng.Pick([12, 24, 24, 48]));
    }

    [Fact]
    public void Fulfillment_showcase_matches_mock_card_for_org_fc2()
    {
        // Значения получены из partners.ts (cardFor) для org_fc2
        var s = FulfillmentShowcase.For("org_fc2");
        Assert.Equal(7, s.Services.Count);
        Assert.Equal(4.5, s.Rating);
        Assert.Equal(19, s.ExtraClients);
        Assert.Equal(["wb", "ozon"], s.Marketplaces);
        Assert.Equal(new FcStartRates(3, 30, 48, 6), s.Rates);
        Assert.Equal(12, s.SlaHours);
    }

    [Fact]
    public void Contract_tariff_is_derived_from_char_codes()
    {
        var seed = "ctr_s1_fc1".Sum(c => (int)c);
        var t = ContractTariff.For("ctr_s1_fc1");
        Assert.Equal(45 + seed % 4 * 5, t.PickPerOrder);
        Assert.Equal(6 + seed % 3, t.ReceivingPerUnit);
    }

    [Fact]
    public void Sorting_puts_nulls_last_and_uses_russian_collation()
    {
        string?[] items = ["Яблоко", null, "арбуз", "Банан"];
        Assert.Equal(["арбуз", "Банан", "Яблоко", null], items.SortBy(x => x, SortOrder.Asc));
        Assert.Equal(["Яблоко", "Банан", "арбуз", null], items.SortBy(x => x, SortOrder.Desc));
    }

    [Theory]
    [InlineData(null, null, 1, 25)]
    [InlineData(0, 500, 1, 200)]
    [InlineData(3, 0, 3, 25)]
    public void Page_query_clamps_like_listParams(int? page, int? size, int expectedPage, int expectedSize)
    {
        var q = new PageQuery(page, size);
        Assert.Equal((expectedPage, expectedSize), (q.SafePage, q.SafePageSize));
    }
}

public class PlatformHandlersTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly RequestContext Admin = new("usr_admin", null, Role.PlatformAdmin, null);
    private static readonly RequestContext FcOwner = new("usr_fc", "org_fc", Role.FcOwner, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly RequestContext SellerOwner = new("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);

    public PlatformHandlersTests()
    {
        _clock.UtcNow.Returns(Now);
        _ids.NewSuffix().Returns("1");
        _db.Organizations.AddRange(
            new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "7700000001", "Москва", OrganizationStatus.Active, Now.AddDays(-10)),
            new Organization("org_s", OrganizationType.Seller, "Селлер", "7700000002", "Казань", OrganizationStatus.Active, Now.AddDays(-5)),
            new Organization("org_p", OrganizationType.Seller, "Новая", "7700000003", "Омск", OrganizationStatus.Pending, Now.AddDays(-1)));
        _db.Users.Add(new User("usr_s", "s@demo.ru", "h", "Сергей", "+7 900", false));
        _db.Users.Add(new User("usr_admin", "admin@demo.ru", "h", "Админ", null, true));
        _db.Warehouses.Add(new Warehouse("wh_1", "org_fc", "Склад", "W1", "Москва", "ул. 1", 1000, true));
        _db.ClientRequests.Add(new ClientRequest("org_fc", "creq_1", "C", "1", "М", "A", "+7", 1, 1, ["wb"], "", Now, null));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Approving_pending_org_creates_default_settings()
    {
        var handler = new ModerateOrganizationHandler(_db, new OrganizationOwnerLookup(_db));
        var dto = await handler.HandleAsync("org_p", "approve", default);
        Assert.Equal(OrganizationStatus.Active, dto.Status);
        Assert.True(await _db.Settings.AnyAsync(s => s.OrganizationId == "org_p"));
    }

    [Theory]
    [InlineData("org_p", "block", 409)]
    [InlineData("org_p", "explode", 400)]
    [InlineData("org_none", "approve", 404)]
    public async Task Moderation_errors_match_mock(string id, string action, int status)
    {
        var handler = new ModerateOrganizationHandler(_db, new OrganizationOwnerLookup(_db));
        var e = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(id, action, default));
        Assert.Equal(status, e.Status);
    }

    [Fact]
    public async Task Duplicate_reference_code_is_conflict()
    {
        var handler = new CreateReferenceHandler(_db, new AuditLog(_db, _clock, _ids));
        await handler.HandleAsync(Admin, new ReferenceInput(ReferenceKind.Units, "pcs", "Штука", null), default);
        var e = await Assert.ThrowsAsync<AppException>(() =>
            handler.HandleAsync(Admin, new ReferenceInput(ReferenceKind.Units, "PCS", "Ещё", null), default));
        Assert.Equal((409, "Код PCS уже есть в справочнике"), (e.Status, e.Title));
    }

    [Fact]
    public async Task Accepting_request_requires_own_warehouses()
    {
        var handler = new DecideClientRequestHandler(_db, _clock);
        var e = await Assert.ThrowsAsync<AppException>(() =>
            handler.HandleAsync(FcOwner, "creq_1", "accept", new DecideClientRequestInput(["wh_foreign"]), default));
        Assert.Equal((422, "Выберите склады для договора"), (e.Status, e.Title));
        var ok = await handler.HandleAsync(FcOwner, "creq_1", "accept", new DecideClientRequestInput(["wh_1"]), default);
        Assert.Equal(ClientRequestStatus.Accepted, ok.Status);
        var again = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(FcOwner, "creq_1", "decline", null, default));
        Assert.Equal(409, again.Status);
    }

    [Fact]
    public async Task Seller_connect_creates_pending_request_and_blocks_duplicates()
    {
        _ids.NewSuffix().Returns("new");
        var builder = new FulfillmentCardBuilder(_db);
        var handler = new ConnectFulfillmentHandler(_db, builder, _clock, _ids);
        var input = new ConnectRequestInput(10.4, 2.5, ["wb"], "  привет ");
        var card = await handler.HandleAsync(SellerOwner, "org_fc", input, default);
        Assert.Equal("pending", card.Connection);
        var stored = await _db.ClientRequests.SingleAsync(r => r.SellerId == "org_s");
        Assert.Equal((10, 3, "привет", "Сергей"), (stored.SkuEstimate, stored.OrdersPerDay, stored.Message, stored.ContactName));
        var e = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(SellerOwner, "org_fc", input, default));
        Assert.Equal((409, "Заявка уже на рассмотрении"), (e.Status, e.Title));
    }

    [Theory]
    [InlineData(0, 1, "wb", "Укажите ассортимент и объём заказов")]
    [InlineData(1, -1, "wb", "Укажите ассортимент и объём заказов")]
    [InlineData(1, 0, null, "Выберите хотя бы один маркетплейс")]
    public async Task Seller_connect_validates_input(double sku, double orders, string? marketplace, string message)
    {
        var handler = new ConnectFulfillmentHandler(_db, new FulfillmentCardBuilder(_db), _clock, _ids);
        var e = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(SellerOwner, "org_fc",
            new ConnectRequestInput(sku, orders, marketplace is null ? [] : [marketplace], null), default));
        Assert.Equal((422, message), (e.Status, e.Title));
    }

    [Theory]
    [InlineData("", "044525853", "Укажите название организации")]
    [InlineData("A", "123", "БИК должен состоять из 9 цифр")]
    public void Profile_validator_uses_mock_messages(string name, string bik, string message)
    {
        var p = new OrgProfile(name, "1", "", "", "", "a@b.ru", "", "", bik, new string('1', 20), new string('2', 20));
        var e = Assert.Throws<AppException>(() => new OrgProfileValidator().Validate(p));
        Assert.Equal((422, message), (e.Status, e.Title));
    }

    [Theory]
    [InlineData("bad", "seller_manager", "Некорректный email")]
    [InlineData("x@y.ru", "fc_picker", "Роль недоступна для организации")]
    [InlineData("x@y.ru", "nonsense", "Роль недоступна для организации")]
    public async Task Invite_validates_email_and_role(string email, string role, string message)
    {
        var e = await Assert.ThrowsAsync<AppException>(() =>
            new InviteMemberHandler(_db, _clock, _ids).HandleAsync(SellerOwner, new InviteInput(email, role), default));
        Assert.Equal((422, message), (e.Status, e.Title));
    }

    [Fact]
    public async Task Invite_then_duplicate_is_conflict()
    {
        var handler = new InviteMemberHandler(_db, _clock, _ids);
        var member = await handler.HandleAsync(SellerOwner, new InviteInput("New@Y.ru", "seller_manager"), default);
        Assert.Equal(("inv_1", "new@y.ru", "invited"), (member.Id, member.Email, member.Status));
        var e = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(SellerOwner, new InviteInput("new@y.ru", "seller_manager"), default));
        Assert.Equal(409, e.Status);
    }
}
