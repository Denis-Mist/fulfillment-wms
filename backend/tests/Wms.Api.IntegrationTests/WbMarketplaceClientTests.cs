using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;
using Wms.Infrastructure.Marketplaces;

namespace Wms.Api.IntegrationTests;

public class WbMarketplaceClientTests
{
    private static MarketplaceCredentials SandboxToken => new("test-token-value-123456", null, MarketplaceEnvironment.Sandbox);

    [Fact]
    public async Task Ping_uses_sandbox_host_and_authorization_header()
    {
        var handler = new ScriptedHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://marketplace-api-sandbox.wildberries.ru/ping", req.RequestUri!.ToString());
            Assert.Equal("test-token-value-123456", req.Headers.GetValues("Authorization").Single());
            return Json(HttpStatusCode.OK, """{"TS":"2026-11-06T00:00:00Z","Status":"OK"}""");
        });
        var client = Create(handler);
        await client.PingAsync(SandboxToken, default);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Ping_401_surfaces_marketplace_text()
    {
        var handler = new ScriptedHandler((_, _) =>
            Json(HttpStatusCode.Unauthorized, """{"title":"unauthorized","detail":"token mismatch token category"}"""));
        var client = Create(handler);
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() => client.PingAsync(SandboxToken, default));
        Assert.Equal(401, ex.HttpStatus);
        Assert.Contains("token mismatch token category", ex.Message);
        Assert.True(ex.IsClientError);
    }

    [Fact]
    public async Task Get_orders_and_cards_parse_counts()
    {
        var handler = new ScriptedHandler((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/api/v3/orders/new")
                return Json(HttpStatusCode.OK, """{"orders":[{"id":1},{"id":2}]}""");
            if (path == "/content/v2/get/cards/list")
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("content-api-sandbox.wildberries.ru", req.RequestUri.Host);
                return Json(HttpStatusCode.OK, """{"cards":[{},{}], "cursor":{"total":7}}""");
            }
            return Json(HttpStatusCode.NotFound, """{"title":"missing"}""");
        });
        var client = Create(handler);
        Assert.Equal(2, await client.GetFbsOrderCountAsync(SandboxToken, default));
        Assert.Equal(7, await client.GetCardCountAsync(SandboxToken, default));
    }

    [Fact]
    public async Task Recent_orders_are_paged_when_there_are_no_new_ones()
    {
        var nexts = new List<string>();
        var handler = new ScriptedHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/v3/orders/new")
                return Json(HttpStatusCode.OK, """{"orders":[]}""");
            nexts.Add(req.RequestUri.Query);
            var page = nexts.Count;
            var orders = page == 1 ? """[{"id":1},{"id":2}]""" : """[{"id":3}]""";
            var next = page == 1 ? 1000 : 0;
            return Json(HttpStatusCode.OK, "{\"orders\":" + orders + ",\"next\":" + next + "}");
        });
        Assert.Equal(3, await Create(handler).GetFbsOrderCountAsync(SandboxToken, default));
        Assert.Contains("next=0", nexts[0]);
        Assert.Contains("limit=1000", nexts[0]);
        Assert.Contains("next=1000", nexts[1]);
    }

    [Fact]
    public async Task Push_stocks_collapses_duplicate_sku_and_batches_by_1000()
    {
        var puts = new List<string?>();
        var handler = new ScriptedHandler((req, body) =>
        {
            if (req.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, """{"warehouses":[{"id":42,"name":"FBS"}]}""");
            puts.Add(body);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = Create(handler);
        Assert.Equal(0, await client.PushStocksAsync(SandboxToken, [], default));
        Assert.Empty(puts);
        var stocks = Enumerable.Range(0, 1000).Select(i => new MarketplaceStockItem($"sku-{i}", 1)).ToList();
        stocks.Add(new MarketplaceStockItem("sku-0", 4));
        Assert.Equal(1000, await client.PushStocksAsync(SandboxToken, stocks, default));
        Assert.Single(puts);
        Assert.Contains("\"sku\":\"sku-0\",\"amount\":5", puts[0]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(puts[0]!, "sku-0"));
    }

    [Fact]
    public async Task Timeout_is_reported_as_marketplace_outage()
    {
        var handler = new ScriptedHandler((_, _) => throw new TaskCanceledException("elapsed"));
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() => Create(handler).PingAsync(SandboxToken, CancellationToken.None));
        Assert.Contains("Нет ответа", ex.Message);
        Assert.False(ex.IsClientError);
    }

    [Fact]
    public async Task Push_stocks_puts_to_first_warehouse()
    {
        var handler = new ScriptedHandler((req, body) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/api/v3/warehouses")
                return Json(HttpStatusCode.OK, """{"warehouses":[{"id":42,"name":"FBS"}]}""");
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("/api/v3/stocks/42", req.RequestUri!.AbsolutePath);
            Assert.Contains("\"sku\":\"12345678\"", body);
            Assert.Contains("\"amount\":5", body);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var client = Create(handler);
        var count = await client.PushStocksAsync(SandboxToken, [new MarketplaceStockItem("12345678", 5)], default);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Production_hosts_are_used_when_environment_is_production()
    {
        var handler = new ScriptedHandler((req, _) =>
        {
            Assert.Equal("marketplace-api.wildberries.ru", req.RequestUri!.Host);
            return Json(HttpStatusCode.OK, """{"Status":"OK"}""");
        });
        var client = Create(handler);
        await client.PingAsync(new MarketplaceCredentials("prod-token-value-123456", null, MarketplaceEnvironment.Production), default);
    }

    [Fact]
    public async Task Create_test_order_posts_to_sandbox_make()
    {
        var handler = new ScriptedHandler((req, body) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("/api/v3/test/fbs/orders/make", req.RequestUri!.AbsolutePath);
            Assert.Contains("\"warehouseId\":42", body);
            return Json(HttpStatusCode.OK, """{"orders":[{"id":99}]}""");
        });
        var client = Create(handler);
        var created = await client.CreateTestFbsOrdersAsync(SandboxToken, [new WbTestOrder("12345678", 1, 42)], default);
        Assert.Equal(1, created);
    }

    private static WbMarketplaceClient Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new MarketplaceOptions()), NullLogger<WbMarketplaceClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, body);
        }
    }
}
