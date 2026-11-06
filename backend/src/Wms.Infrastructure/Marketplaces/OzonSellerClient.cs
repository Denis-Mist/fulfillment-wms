using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Integrations;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Marketplaces;

/// <summary>
/// HTTP-клиент Ozon Seller API. Отдельного sandbox URL в официальной документации нет
/// (https://docs.ozon.ru/api/seller/) — запросы идут на https://api-seller.ozon.ru.
/// Режим Sandbox означает «тест»: только чтение, выгрузка остатков — если AllowProductionStockPush.
/// </summary>
public sealed class OzonSellerClient(HttpClient http, IOptions<MarketplaceOptions> options, ILogger<OzonSellerClient> logger) : IMarketplaceClient
{
    public IntegrationChannel Channel => IntegrationChannel.Ozon;

    public Task PingAsync(MarketplaceCredentials credentials, CancellationToken ct) =>
        PostAsync(credentials, "/v1/roles", new { }, ct);

    public async Task<int> GetFbsOrderCountAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var (since, to) = Window();
        var fbs = await PostCountAsync(credentials, "/v4/posting/fbs/list", new
        {
            dir = "ASC",
            filter = new { since, to },
            limit = 100,
            offset = 0,
        }, ct, "postings", "items");
        // FBO читаем всегда (требование интеграции); в счётчик FBS не смешиваем.
        _ = await PostCountAsync(credentials, "/v2/posting/fbo/list", new
        {
            dir = "ASC",
            filter = new { since, to },
            limit = 100,
            offset = 0,
        }, ct, "postings", "items");
        return fbs;
    }

    public async Task<int> GetCardCountAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var json = await PostAsync(credentials, "/v3/product/list", new
        {
            filter = new { visibility = "ALL" },
            last_id = "",
            limit = 100,
        }, ct);
        if (TryNested(json, out var result, "result") && result.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
            return total.GetInt32();
        if (TryNested(json, out result, "result") && result.TryGetProperty("total_items", out var totalItems) && totalItems.ValueKind == JsonValueKind.Number)
            return totalItems.GetInt32();
        return CountItems(json, "items", "products");
    }

    public async Task<int> PushStocksAsync(MarketplaceCredentials credentials, IReadOnlyList<MarketplaceStockItem> stocks, CancellationToken ct)
    {
        var warehouseId = await FirstWarehouseIdAsync(credentials, ct);
        if (warehouseId is null)
            throw new MarketplaceApiException("В кабинете Ozon нет склада — выгрузка остатков невозможна.", 422);
        if (stocks.Count == 0) return 0;

        await PostAsync(credentials, "/v2/products/stocks", new
        {
            stocks = stocks.Select(s => new { offer_id = s.Sku, stock = s.Amount, warehouse_id = warehouseId.Value }).ToList(),
        }, ct);
        return stocks.Count;
    }

    private async Task<long?> FirstWarehouseIdAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var json = await PostAsync(credentials, "/v1/warehouse/list", new { }, ct);
        var array = MarketplaceHttp.Array(json, "result", "warehouses") ?? (TryNested(json, out var result, "result") ? MarketplaceHttp.Array(result, "warehouses") : null);
        if (array is null) return null;
        foreach (var item in array.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("warehouse_id", out var id) && id.TryGetInt64(out var value)) return value;
            if (item.TryGetProperty("id", out id) && id.TryGetInt64(out value)) return value;
        }
        return null;
    }

    private async Task<int> PostCountAsync(MarketplaceCredentials credentials, string path, object body, CancellationToken ct, params string[] names)
    {
        var json = await PostAsync(credentials, path, body, ct);
        if (TryNested(json, out var result, "result"))
        {
            var inner = CountItems(result, names);
            if (inner > 0) return inner;
        }
        return CountItems(json, names);
    }

    private async Task<JsonElement> PostAsync(MarketplaceCredentials credentials, string path, object body, CancellationToken ct)
    {
        var url = new Uri(BaseUri(credentials), path.TrimStart('/'));
        logger.LogInformation("Ozon POST {Url}", url);
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = MarketplaceHttp.JsonBody(body) };
        if (!string.IsNullOrWhiteSpace(credentials.ClientId))
            req.Headers.TryAddWithoutValidation("Client-Id", credentials.ClientId);
        req.Headers.TryAddWithoutValidation("Api-Key", credentials.ApiKey);
        return await MarketplaceHttp.SendAsync(http, req, "Ozon", ct);
    }

    private Uri BaseUri(MarketplaceCredentials credentials)
    {
        var hosts = options.Value.Ozon;
        var raw = credentials.Environment == MarketplaceEnvironment.Sandbox && !string.IsNullOrWhiteSpace(hosts.SandboxBaseUrl)
            ? hosts.SandboxBaseUrl
            : hosts.BaseUrl;
        return new Uri(raw.TrimEnd('/') + "/");
    }

    private static (string since, string to) Window()
    {
        var to = DateTimeOffset.UtcNow;
        var since = to.AddDays(-7);
        return (since.ToString("yyyy-MM-ddTHH:mm:ssZ"), to.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    private static bool TryNested(JsonElement root, out JsonElement child, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out child)) return true;
        child = default;
        return false;
    }

    private static int CountItems(JsonElement root, params string[] names)
    {
        var count = MarketplaceHttp.ArrayCount(root, names);
        if (count > 0) return count;
        if (TryNested(root, out var result, "result")) return MarketplaceHttp.ArrayCount(result, names);
        return 0;
    }
}
