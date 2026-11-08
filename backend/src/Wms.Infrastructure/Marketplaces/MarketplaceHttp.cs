using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wms.Application.Integrations;

namespace Wms.Infrastructure.Marketplaces;

internal static class MarketplaceHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static async Task<JsonElement> SendAsync(HttpClient http, HttpRequestMessage request, string marketplace, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MarketplaceApiException($"Нет ответа API {marketplace}: {ex.Message}", (int)HttpStatusCode.ServiceUnavailable);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new MarketplaceApiException(FormatError(marketplace, (int)response.StatusCode, text), (int)response.StatusCode, text);
            if (string.IsNullOrWhiteSpace(text)) return default;
            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new MarketplaceApiException($"{marketplace} вернул не JSON (HTTP {(int)response.StatusCode})", (int)response.StatusCode, text);
            }
        }
    }

    public static HttpContent JsonBody(object body) => JsonContent.Create(body, options: Json);

    /// <summary>Складывает повторяющиеся SKU. Маркетплейс отклоняет пачку с одним артикулом дважды.</summary>
    public static IReadOnlyList<MarketplaceStockItem> CollapseSkus(IReadOnlyList<MarketplaceStockItem> stocks)
    {
        if (stocks.Count == 0) return stocks;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = true;
        foreach (var item in stocks)
        {
            if (string.IsNullOrWhiteSpace(item.Sku) || item.Amount < 0 || !seen.Add(item.Sku))
            {
                unique = false;
                break;
            }
        }
        if (unique) return stocks;

        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in stocks)
        {
            if (string.IsNullOrWhiteSpace(item.Sku)) continue;
            map[item.Sku] = Math.Min(int.MaxValue, map.GetValueOrDefault(item.Sku) + Math.Max(0, item.Amount));
        }
        return map.Select(kv => new MarketplaceStockItem(kv.Key, (int)kv.Value)).ToList();
    }

    public static string ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Пустой ответ API";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (ReadMessage(doc.RootElement) is { Length: > 0 } found) return found;
        }
        catch (JsonException) { /* берём сырое тело */ }
        return body.Length <= 500 ? body : body[..500];
    }

    public static string? ReadMessage(JsonElement node) => FindMessage(node, 0);

    public static bool TryInt(JsonElement el, out int value)
    {
        value = 0;
        if (el.ValueKind != JsonValueKind.Number) return false;
        if (el.TryGetInt32(out value)) return true;
        if (el.TryGetInt64(out var wide))
        {
            value = (int)Math.Clamp(wide, 0, int.MaxValue);
            return true;
        }
        if (el.TryGetDouble(out var number) && double.IsFinite(number))
        {
            value = (int)Math.Clamp(number, 0, int.MaxValue);
            return true;
        }
        return false;
    }

    public static bool TryInt64(JsonElement root, string name, out long value)
    {
        value = 0;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var el)) return false;
        if (el.TryGetInt64(out value)) return true;
        if (el.TryGetInt32(out var narrow))
        {
            value = narrow;
            return true;
        }
        return false;
    }

    public static string FormatError(string marketplace, int status, string? body) =>
        $"{marketplace} (HTTP {status}): {ExtractMessage(body)}";

    public static int ArrayCount(JsonElement root, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value.GetArrayLength();
        }
        return root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : 0;
    }

    public static JsonElement? Array(JsonElement root, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }
        return root.ValueKind == JsonValueKind.Array ? root : null;
    }

    private static readonly string[] MessageFields = ["detail", "title", "message", "errorText", "reason", "error"];

    private static string? FindMessage(JsonElement node, int depth)
    {
        if (depth > 5) return null;
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                var text = node.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    if (FindMessage(item, depth + 1) is { } nested) return nested;
                }
                return null;
            case JsonValueKind.Object:
                foreach (var name in MessageFields)
                {
                    if (node.TryGetProperty(name, out var value) && FindMessage(value, depth + 1) is { } nested)
                        return nested;
                }
                if (node.TryGetProperty("errors", out var errors)) return FindMessage(errors, depth + 1);
                return null;
            default:
                return null;
        }
    }
}
