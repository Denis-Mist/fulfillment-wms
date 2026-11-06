using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Marketplaces;

/// <summary>
/// HTTP-клиент WB API. В Development по умолчанию ходит на sandbox-хосты
/// (marketplace-api-sandbox, content-api-sandbox, statistics-api-sandbox).
/// </summary>
public sealed class WbMarketplaceClient(HttpClient http, IOptions<MarketplaceOptions> options, ILogger<WbMarketplaceClient> logger) : IMarketplaceClient
{
    public IntegrationChannel Channel => IntegrationChannel.Wb;

    public async Task PingAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var url = new Uri(Hosts().Marketplace(credentials.Environment), "ping");
        logger.LogInformation("WB ping {Url}", url);
        using var req = Authorized(HttpMethod.Get, url, credentials);
        await MarketplaceHttp.SendAsync(http, req, "Wildberries", ct);
    }

    public async Task<int> GetFbsOrderCountAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var mp = Hosts().Marketplace(credentials.Environment);
        var fresh = await GetAsync(credentials, new Uri(mp, "api/v3/orders/new"), ct);
        var count = MarketplaceHttp.ArrayCount(fresh, "orders");
        if (count > 0) return count;

        var from = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds();
        var recent = await GetAsync(credentials, new Uri(mp, $"api/v3/orders?limit=100&next=0&dateFrom={from}"), ct);
        return MarketplaceHttp.ArrayCount(recent, "orders");
    }

    public async Task<int> GetCardCountAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var url = new Uri(Hosts().Content(credentials.Environment), "content/v2/get/cards/list");
        var body = new { settings = new { cursor = new { limit = 100 }, filter = new { withPhoto = -1 } } };
        logger.LogInformation("WB cards {Url}", url);
        using var req = Authorized(HttpMethod.Post, url, credentials, MarketplaceHttp.JsonBody(body));
        var json = await MarketplaceHttp.SendAsync(http, req, "Wildberries", ct);
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("cursor", out var cursor)
            && cursor.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
            return total.GetInt32();
        return MarketplaceHttp.ArrayCount(json, "cards");
    }

    public async Task<int> PushStocksAsync(MarketplaceCredentials credentials, IReadOnlyList<MarketplaceStockItem> stocks, CancellationToken ct)
    {
        var warehouses = await ListWarehousesAsync(credentials, ct);
        if (warehouses.Count == 0)
            throw new MarketplaceApiException("На маркетплейсе нет склада продавца — создайте склад в sandbox WB.", 422);
        if (stocks.Count == 0) return 0;

        var warehouseId = warehouses[0];
        var url = new Uri(Hosts().Marketplace(credentials.Environment), $"api/v3/stocks/{warehouseId}");
        var payload = new { stocks = stocks.Select(s => new { sku = s.Sku, amount = s.Amount }).ToList() };
        logger.LogInformation("WB push stocks warehouse {Warehouse} sku {Count}", warehouseId, stocks.Count);
        using var req = Authorized(HttpMethod.Put, url, credentials, MarketplaceHttp.JsonBody(payload));
        await MarketplaceHttp.SendAsync(http, req, "Wildberries", ct);
        return stocks.Count;
    }

    /// <summary>Создание тестового FBS-заказа в sandbox (POST /api/v3/test/fbs/orders/make). Не вызывается при обычном sync.</summary>
    public async Task<int> CreateTestFbsOrdersAsync(MarketplaceCredentials credentials, IReadOnlyList<WbTestOrder> orders, CancellationToken ct)
    {
        if (credentials.Environment != MarketplaceEnvironment.Sandbox)
            throw new MarketplaceApiException("Создание тестового заказа доступно только в sandbox WB.", 422);
        var url = new Uri(Hosts().Marketplace(credentials.Environment), "api/v3/test/fbs/orders/make");
        var payload = new { orders = orders.Select(o => new { sku = o.Sku, amount = o.Amount, warehouseId = o.WarehouseId }).ToList() };
        using var req = Authorized(HttpMethod.Post, url, credentials, MarketplaceHttp.JsonBody(payload));
        var json = await MarketplaceHttp.SendAsync(http, req, "Wildberries", ct);
        return MarketplaceHttp.ArrayCount(json, "orders", "data");
    }

    public async Task<IReadOnlyList<long>> ListWarehousesAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var json = await GetAsync(credentials, new Uri(Hosts().Marketplace(credentials.Environment), "api/v3/warehouses"), ct);
        var array = MarketplaceHttp.Array(json, "warehouses");
        if (array is null) return [];
        var ids = new List<long>();
        foreach (var item in array.Value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.TryGetInt64(out var value))
                ids.Add(value);
            else if (item.TryGetInt64(out var raw))
                ids.Add(raw);
        }
        return ids;
    }

    private async Task<JsonElement> GetAsync(MarketplaceCredentials credentials, Uri url, CancellationToken ct)
    {
        logger.LogInformation("WB GET {Url}", url);
        using var req = Authorized(HttpMethod.Get, url, credentials);
        return await MarketplaceHttp.SendAsync(http, req, "Wildberries", ct);
    }

    private MarketplaceOptions.WbHosts Hosts() => options.Value.Wb;

    private static HttpRequestMessage Authorized(HttpMethod method, Uri url, MarketplaceCredentials credentials, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.TryAddWithoutValidation("Authorization", credentials.ApiKey);
        return req;
    }
}

public sealed record WbTestOrder(string Sku, int Amount, long WarehouseId);
