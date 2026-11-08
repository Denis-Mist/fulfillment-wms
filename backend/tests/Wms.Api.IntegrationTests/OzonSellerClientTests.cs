using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;
using Wms.Infrastructure.Marketplaces;

namespace Wms.Api.IntegrationTests;

public class OzonSellerClientTests
{
    private static MarketplaceCredentials Creds =>
        new("ozon-api-key-value-123456", "123456", MarketplaceEnvironment.Sandbox);

    [Fact]
    public async Task Ping_posts_roles_with_client_headers()
    {
        var handler = new ScriptedHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://api-seller.ozon.ru/v1/roles", req.RequestUri!.ToString());
            Assert.Equal("123456", req.Headers.GetValues("Client-Id").Single());
            Assert.Equal("ozon-api-key-value-123456", req.Headers.GetValues("Api-Key").Single());
            return Json(HttpStatusCode.OK, """{"roles":[{"name":"Admin"}],"expires_at":"2027-01-01T00:00:00Z"}""");
        });
        await Create(handler).PingAsync(Creds, default);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Ping_403_surfaces_ozon_message()
    {
        var handler = new ScriptedHandler((_, _) =>
            Json(HttpStatusCode.Forbidden, """{"code":5,"message":"Invalid Api-Key"}"""));
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() => Create(handler).PingAsync(Creds, default));
        Assert.Equal(403, ex.HttpStatus);
        Assert.Contains("Invalid Api-Key", ex.Message);
    }

    [Fact]
    public async Task Ping_403_reads_nested_error_object()
    {
        var handler = new ScriptedHandler((_, _) =>
            Json(HttpStatusCode.Forbidden, """{"error":{"code":5,"message":"Client-Id is missing"}}"""));
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() => Create(handler).PingAsync(Creds, default));
        Assert.Contains("Client-Id is missing", ex.Message);
    }

    [Fact]
    public async Task Lists_products_and_fbs_then_fbo_postings()
    {
        var paths = new List<string>();
        var handler = new ScriptedHandler((req, _) =>
        {
            paths.Add(req.RequestUri!.AbsolutePath);
            return req.RequestUri.AbsolutePath switch
            {
                "/v3/product/list" => Json(HttpStatusCode.OK, """{"result":{"items":[{},{}], "total":4}}"""),
                "/v4/posting/fbs/list" => Json(HttpStatusCode.OK, """{"result":{"postings":[{},{},{}]}}"""),
                "/v2/posting/fbo/list" => Json(HttpStatusCode.OK, """{"result":{"postings":[{}]}}"""),
                _ => Json(HttpStatusCode.NotFound, """{"message":"unexpected"}"""),
            };
        });
        var client = Create(handler);
        Assert.Equal(4, await client.GetCardCountAsync(Creds, default));
        Assert.Equal(3, await client.GetFbsOrderCountAsync(Creds, default));
        Assert.Equal(["/v3/product/list", "/v4/posting/fbs/list", "/v2/posting/fbo/list"], paths);
    }

    [Fact]
    public async Task Fbs_count_follows_has_next_pages()
    {
        var offsets = new List<string?>();
        var handler = new ScriptedHandler((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath == "/v2/posting/fbo/list")
                return Json(HttpStatusCode.OK, """{"result":{"postings":[]}}""");
            offsets.Add(body);
            var offset = offsets.Count == 1 ? 0 : 100;
            Assert.Contains($"\"offset\":{offset}", body);
            var hasNext = offsets.Count == 1 ? "true" : "false";
            var postings = offsets.Count == 1 ? "[{},{}]" : "[{}]";
            return Json(HttpStatusCode.OK, "{\"result\":{\"postings\":" + postings + ",\"has_next\":" + hasNext + "}}");
        });
        Assert.Equal(3, await Create(handler).GetFbsOrderCountAsync(Creds, default));
        Assert.Equal(2, offsets.Count);
    }

    [Fact]
    public async Task Push_stocks_skips_disabled_warehouse_and_reports_item_errors()
    {
        var handler = new ScriptedHandler((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath == "/v1/warehouse/list")
                return Json(HttpStatusCode.OK, """{"result":[{"warehouse_id":1,"status":"disabled"},{"warehouse_id":9,"status":"created"}]}""");
            Assert.Contains("\"warehouse_id\":9", body);
            return Json(HttpStatusCode.OK, """{"result":[{"offer_id":"SKU-1","updated":false,"errors":[{"message":"product not found"}]}]}""");
        });
        var ex = await Assert.ThrowsAsync<MarketplaceApiException>(() =>
            Create(handler).PushStocksAsync(Creds, [new MarketplaceStockItem("SKU-1", 2), new MarketplaceStockItem("SKU-1", 3)], default));
        Assert.Contains("product not found", ex.Message);
        Assert.Contains("0 из 1", ex.Message);
    }

    [Fact]
    public async Task Push_stocks_splits_batches_of_100_and_ignores_empty_catalog()
    {
        var stockCalls = 0;
        var handler = new ScriptedHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath == "/v1/warehouse/list")
                return Json(HttpStatusCode.OK, """{"result":[{"warehouse_id":77}]}""");
            stockCalls++;
            return Json(HttpStatusCode.OK, """{"result":[]}""");
        });
        var client = Create(handler);
        Assert.Equal(0, await client.PushStocksAsync(Creds, [], default));
        Assert.Equal(0, stockCalls);
        var stocks = Enumerable.Range(0, 101).Select(i => new MarketplaceStockItem($"SKU-{i}", 1)).ToList();
        Assert.Equal(101, await client.PushStocksAsync(Creds, stocks, default));
        Assert.Equal(2, stockCalls);
    }

    [Fact]
    public async Task Cancelled_call_is_not_a_marketplace_outage()
    {
        var handler = new ScriptedHandler((_, _) => throw new TaskCanceledException("aborted"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).PingAsync(Creds, cts.Token));
    }

    [Fact]
    public async Task Push_stocks_writes_only_after_warehouse_lookup()
    {
        var handler = new ScriptedHandler((req, body) =>
        {
            if (req.RequestUri!.AbsolutePath == "/v1/warehouse/list")
                return Json(HttpStatusCode.OK, """{"result":[{"warehouse_id":77,"name":"FBS"}]}""");
            Assert.Equal("/v2/products/stocks", req.RequestUri.AbsolutePath);
            Assert.Contains("\"offer_id\":\"SKU-1\"", body);
            Assert.Contains("\"stock\":8", body);
            Assert.Contains("\"warehouse_id\":77", body);
            return Json(HttpStatusCode.OK, """{"result":[]}""");
        });
        var count = await Create(handler).PushStocksAsync(Creds, [new MarketplaceStockItem("SKU-1", 8)], default);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Sandbox_without_configured_url_still_uses_seller_api()
    {
        var handler = new ScriptedHandler((req, _) =>
        {
            Assert.Equal("api-seller.ozon.ru", req.RequestUri!.Host);
            return Json(HttpStatusCode.OK, "{}");
        });
        await Create(handler).PingAsync(Creds, default);
    }

    private static OzonSellerClient Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new MarketplaceOptions()), NullLogger<OzonSellerClient>.Instance);

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
