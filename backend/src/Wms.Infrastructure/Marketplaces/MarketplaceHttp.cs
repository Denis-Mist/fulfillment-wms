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
            try { return JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException)
            {
                throw new MarketplaceApiException($"{marketplace} вернул не JSON (HTTP {(int)response.StatusCode})", (int)response.StatusCode, text);
            }
        }
    }

    public static HttpContent JsonBody(object body) => JsonContent.Create(body, options: Json);

    public static string ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Пустой ответ API";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var name in new[] { "detail", "title", "message", "errorText", "error", "reason" })
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        catch (JsonException) { /* берём сырое тело */ }
        return body.Length <= 500 ? body : body[..500];
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
}
