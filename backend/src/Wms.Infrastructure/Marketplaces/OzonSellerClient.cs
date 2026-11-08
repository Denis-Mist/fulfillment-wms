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
    /// <summary>Лимит /v2/products/stocks и страницы списка отправлений.</summary>
    public const int PageSize = 100;
    public const int MaxPages = 20;
    private static readonly HashSet<string> ActiveWarehouseStatuses = new(StringComparer.OrdinalIgnoreCase) { "created", "new" };

    public IntegrationChannel Channel => IntegrationChannel.Ozon;

    public Task PingAsync(MarketplaceCredentials credentials, CancellationToken ct) =>
        PostAsync(credentials, "/v1/roles", new { }, ct);

    public async Task<int> GetFbsOrderCountAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var (since, to) = Window();
        var fbs = await CountFbsPagesAsync(credentials, since, to, ct);
        // FBO читаем всегда (требование интеграции); в счётчик FBS не смешиваем.
        _ = await PostCountAsync(credentials, "/v2/posting/fbo/list", PostingQuery(since, to, 0), ct, "postings", "items");
        return fbs;
    }

    public async Task<int> GetCardCountAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var json = await PostAsync(credentials, "/v3/product/list", new
        {
            filter = new { visibility = "ALL" },
            last_id = "",
            limit = PageSize,
        }, ct);
        if (TryNested(json, out var result, "result") && result.TryGetProperty("total", out var total) && MarketplaceHttp.TryInt(total, out var count))
            return count;
        if (TryNested(json, out result, "result") && result.TryGetProperty("total_items", out var totalItems) && MarketplaceHttp.TryInt(totalItems, out var totalCount))
            return totalCount;
        return CountItems(json, "items", "products");
    }

    public async Task<int> PushStocksAsync(MarketplaceCredentials credentials, IReadOnlyList<MarketplaceStockItem> stocks, CancellationToken ct)
    {
        var items = MarketplaceHttp.CollapseSkus(stocks);
        if (items.Count == 0) return 0;
        var warehouseId = await FirstWarehouseIdAsync(credentials, ct);
        if (warehouseId is null)
            throw new MarketplaceApiException("В кабинете Ozon нет склада — выгрузка остатков невозможна.", 422);

        var accepted = 0;
        string? failure = null;
        foreach (var chunk in items.Chunk(PageSize))
        {
            var json = await PostAsync(credentials, "/v2/products/stocks", new
            {
                stocks = chunk.Select(s => new { offer_id = s.Sku, stock = s.Amount, warehouse_id = warehouseId.Value }).ToList(),
            }, ct);
            var (ok, error) = InspectStockResult(json, chunk.Length);
            accepted += ok;
            failure ??= error;
        }
        if (failure is not null)
            throw new MarketplaceApiException($"Ozon принял {accepted} из {items.Count}: {failure}", 422);
        return accepted;
    }

    private async Task<int> CountFbsPagesAsync(MarketplaceCredentials credentials, string since, string to, CancellationToken ct)
    {
        var total = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var json = await PostAsync(credentials, "/v4/posting/fbs/list", PostingQuery(since, to, page * PageSize), ct);
            var count = CountItems(json, "postings", "items");
            total += count;
            if (count == 0 || !HasNext(json)) break;
        }
        return total;
    }

    private async Task<long?> FirstWarehouseIdAsync(MarketplaceCredentials credentials, CancellationToken ct)
    {
        var json = await PostAsync(credentials, "/v1/warehouse/list", new { }, ct);
        var array = MarketplaceHttp.Array(json, "result", "warehouses") ?? (TryNested(json, out var result, "result") ? MarketplaceHttp.Array(result, "warehouses") : null);
        if (array is null) return null;
        long? fallback = null;
        foreach (var item in array.Value.EnumerateArray())
        {
            if (!TryWarehouseId(item, out var value)) continue;
            if (IsActiveWarehouse(item)) return value;
            fallback ??= value;
        }
        return fallback;
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

    private static object PostingQuery(string since, string to, int offset) => new
    {
        dir = "ASC",
        filter = new { since, to },
        limit = PageSize,
        offset,
    };

    private static (string since, string to) Window()
    {
        var to = DateTimeOffset.UtcNow;
        var since = to.AddDays(-7);
        return (since.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), to.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }

    private static bool TryNested(JsonElement root, out JsonElement child, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out child)) return true;
        child = default;
        return false;
    }

    private static bool HasNext(JsonElement json)
    {
        if (TryNested(json, out var result, "result") && HasNextFlag(result)) return true;
        return HasNextFlag(json);
    }

    private static bool HasNextFlag(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty("has_next", out var flag) && flag.ValueKind == JsonValueKind.True;

    private static bool TryWarehouseId(JsonElement item, out long value)
    {
        value = 0;
        if (item.ValueKind != JsonValueKind.Object) return false;
        if (item.TryGetProperty("warehouse_id", out var warehouse) && warehouse.TryGetInt64(out value)) return true;
        if (item.TryGetProperty("id", out var id) && id.TryGetInt64(out value)) return true;
        return false;
    }

    private static bool IsActiveWarehouse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            return true;
        var text = status.GetString();
        return text is not null && ActiveWarehouseStatuses.Contains(text);
    }

    private static (int Accepted, string? Error) InspectStockResult(JsonElement json, int requested)
    {
        if (!TryNested(json, out var result, "result") || result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            return (requested, null);
        var ok = 0;
        string? error = null;
        foreach (var item in result.EnumerateArray())
        {
            if (IsStockAccepted(item)) ok++;
            else error ??= StockError(item);
        }
        return (ok, error);
    }

    private static bool IsStockAccepted(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return true;
        if (item.TryGetProperty("updated", out var updated) && updated.ValueKind == JsonValueKind.False) return false;
        return !item.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() == 0;
    }

    private static string StockError(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("errors", out var errors) && MarketplaceHttp.ReadMessage(errors) is { Length: > 0 } text)
            return text;
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("offer_id", out var offer) && offer.ValueKind == JsonValueKind.String && offer.GetString() is { Length: > 0 } sku)
            return $"позиция {sku} отклонена";
        return "позиция отклонена";
    }

    private static int CountItems(JsonElement root, params string[] names)
    {
        var count = MarketplaceHttp.ArrayCount(root, names);
        if (count > 0) return count;
        if (TryNested(root, out var result, "result")) return MarketplaceHttp.ArrayCount(result, names);
        return 0;
    }
}
