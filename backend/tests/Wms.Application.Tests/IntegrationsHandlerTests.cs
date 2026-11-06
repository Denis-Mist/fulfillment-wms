using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Integrations;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class IntegrationsHandlerTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IMarketplaceClient _wb = Substitute.For<IMarketplaceClient>();
    private readonly IMarketplaceClientResolver _resolver = Substitute.For<IMarketplaceClientResolver>();
    private readonly IMarketplaceSettings _settings = Substitute.For<IMarketplaceSettings>();
    private readonly ISecretProtector _secrets = Substitute.For<ISecretProtector>();

    public IntegrationsHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        _settings.DefaultEnvironment.Returns(MarketplaceEnvironment.Sandbox);
        _secrets.Protect(Arg.Any<string>()).Returns(c => "enc:" + c.Arg<string>());
        _secrets.TryUnprotect(Arg.Any<string?>()).Returns(c =>
        {
            var value = c.Arg<string?>();
            return value?.StartsWith("enc:", StringComparison.Ordinal) == true ? value[4..] : null;
        });
        _wb.Channel.Returns(IntegrationChannel.Wb);
        _resolver.For(IntegrationChannel.Wb).Returns(_wb);
    }

    private static RequestContext Seller(string org = "org_s") =>
        new("usr_s", org, Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);

    private IntegrationsHandler Handler(TestAppDbContext db) =>
        new(db, db, _clock, _resolver, _secrets, _settings);

    [Fact]
    public async Task Sync_without_stored_key_returns_422_and_marks_error()
    {
        await using var db = new TestAppDbContext();
        var row = new MarketplaceIntegration("org_s", IntegrationChannel.Wb);
        row.Restore("••••abcd", null, _clock.UtcNow, _clock.UtcNow, false, []);
        db.Integrations.Add(row);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<AppException>(() => Handler(db).SyncAsync(Seller(), "wb", default));
        Assert.Equal(422, ex.Status);
        Assert.Equal("Ключ API не сохранён", ex.Title);
        var stored = await db.Integrations.FindAsync("org_s", IntegrationChannel.Wb);
        Assert.Equal(IntegrationStatus.Error, stored!.Status);
        Assert.Contains("Подключите кабинет заново", stored.LastError);
        Assert.Equal(IntegrationLogKind.Error, stored.Log[0].Kind);
        await _wb.DidNotReceive().PingAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_stores_encrypted_key_and_sync_calls_wb_client()
    {
        await using var db = new TestAppDbContext();
        db.Integrations.Add(new MarketplaceIntegration("org_s", IntegrationChannel.Wb));
        db.Integrations.Add(new MarketplaceIntegration("org_s", IntegrationChannel.Ozon));
        await db.SaveChangesAsync();

        _wb.GetFbsOrderCountAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>()).Returns(4);
        _wb.GetCardCountAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>()).Returns(9);

        var handler = Handler(db);
        var connected = await handler.ConnectAsync(Seller(), "wb", new ConnectInput(new string('k', 20) + "WXYZ", null), default);
        Assert.Equal("••••WXYZ", connected.KeyMask);
        Assert.Equal(MarketplaceEnvironment.Sandbox, connected.Environment);
        Assert.StartsWith("enc:", (await db.Integrations.FindAsync("org_s", IntegrationChannel.Wb))!.ApiKeyCipher);

        var synced = await handler.SyncAsync(Seller(), "wb", default);
        Assert.Equal(IntegrationStatus.Connected, synced.Status);
        Assert.Equal(4, synced.Log[0].Count);
        Assert.Equal(IntegrationLogKind.Orders, synced.Log[0].Kind);
        Assert.Equal(200, synced.LastHttpStatus);
        Assert.Null(synced.LastError);
        await _wb.Received(1).PingAsync(Arg.Is<MarketplaceCredentials>(c => c.ApiKey.EndsWith("WXYZ") && c.Environment == MarketplaceEnvironment.Sandbox), Arg.Any<CancellationToken>());
        await _wb.Received(1).GetCardCountAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>());
        await _wb.DidNotReceive().PushStocksAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<IReadOnlyList<MarketplaceStockItem>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sync_maps_marketplace_4xx_to_422_and_5xx_to_502()
    {
        await using var db = new TestAppDbContext();
        var row = new MarketplaceIntegration("org_s", IntegrationChannel.Wb);
        row.Connect(new string('a', 24), null, _clock.UtcNow);
        row.SetApiKeyCipher("enc:token-value");
        db.Integrations.Add(row);
        await db.SaveChangesAsync();

        _wb.PingAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new MarketplaceApiException("Wildberries (HTTP 401): unauthorized", 401, """{"title":"unauthorized"}""")));

        var ex = await Assert.ThrowsAsync<AppException>(() => Handler(db).SyncAsync(Seller(), "wb", default));
        Assert.Equal(422, ex.Status);
        Assert.Equal("Ошибка API маркетплейса", ex.Title);
        Assert.Contains("unauthorized", ex.Detail);
        Assert.Equal(401, (await db.Integrations.FindAsync("org_s", IntegrationChannel.Wb))!.LastHttpStatus);

        _wb.PingAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new MarketplaceApiException("Wildberries (HTTP 503): down", 503)));
        var gateway = await Assert.ThrowsAsync<AppException>(() => Handler(db).SyncAsync(Seller(), "wb", default));
        Assert.Equal(502, gateway.Status);
    }

    [Fact]
    public async Task Ozon_sync_uses_client_after_connect()
    {
        var ozon = Substitute.For<IMarketplaceClient>();
        ozon.Channel.Returns(IntegrationChannel.Ozon);
        _resolver.For(IntegrationChannel.Ozon).Returns(ozon);
        ozon.GetFbsOrderCountAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>()).Returns(2);
        ozon.GetCardCountAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<CancellationToken>()).Returns(6);

        await using var db = new TestAppDbContext();
        db.Integrations.Add(new MarketplaceIntegration("org_s", IntegrationChannel.Ozon));
        await db.SaveChangesAsync();

        var handler = Handler(db);
        await handler.ConnectAsync(Seller(), "ozon", new ConnectInput(new string('k', 24), "123456"), default);
        var synced = await handler.SyncAsync(Seller(), "ozon", default);
        Assert.Equal(2, synced.Log[0].Count);
        await ozon.Received(1).PingAsync(Arg.Is<MarketplaceCredentials>(c => c.ClientId == "123456"), Arg.Any<CancellationToken>());
        await ozon.DidNotReceive().PushStocksAsync(Arg.Any<MarketplaceCredentials>(), Arg.Any<IReadOnlyList<MarketplaceStockItem>>(), Arg.Any<CancellationToken>());
    }
}
