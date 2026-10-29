using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Wms.Application.Common;

namespace Wms.Api.Infrastructure;

/// <summary>
/// Тело читается после проверки доступа (как в моках: сначала withAuth, потом request.json()).
/// Пустое тело → null, кривой JSON → 400.
/// </summary>
public static class Body
{
    public static async Task<T?> ReadAsync<T>(HttpContext http, CancellationToken ct) where T : class
    {
        if (http.Request.ContentLength is 0 || !(http.Request.HasJsonContentType() || http.Request.ContentLength > 0)) return null;
        var options = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        try
        {
            var node = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct);
            EnsureLengths(node);
            return node is null ? null : node.Deserialize<T>(options);
        }
        catch (JsonException)
        {
            throw AppException.BadRequest("Некорректный запрос", "Тело запроса не является корректным JSON");
        }
    }

    /// <summary>
    /// Как <see cref="ReadAsync{T}"/>, но пустое тело превращается в «пустой объект» (все поля по умолчанию),
    /// чтобы обязательность полей проверял обработчик (400/422), а не биндинг до авторизации.
    /// Используется вместо параметра-тела в эндпоинтах: биндинг minimal API выполняется до фильтров доступа.
    /// </summary>
    public static async Task<T> ReadOrEmptyAsync<T>(HttpContext http, CancellationToken ct) where T : class =>
        await ReadAsync<T>(http, ct) ?? Empty<T>(http);

    /// <summary>Общий лимит длины строк (TextLimits) по всем полям тела, после проверки доступа → 422 с ошибками по полям.</summary>
    private static void EnsureLengths(JsonNode? root)
    {
        var errors = new Dictionary<string, string[]>();
        void Walk(JsonNode? node, string path, string field)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (key, child) in obj) Walk(child, path.Length == 0 ? key : $"{path}.{key}", key);
                    break;
                case JsonArray arr:
                    for (var i = 0; i < arr.Count; i++) Walk(arr[i], $"{path}[{i}]", field);
                    break;
                case JsonValue value when value.TryGetValue<string>(out var s):
                    TextLimits.Check(errors, path.Length == 0 ? "body" : path, s, TextLimits.MaxFor(field));
                    break;
            }
        }
        Walk(root, string.Empty, string.Empty);
        if (errors.Count > 0) throw new AppException(422, TextLimits.TooLongTitle, "Сократите значения полей.", errors);
    }

    private static T Empty<T>(HttpContext http) where T : class
    {
        var options = http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        return JsonSerializer.Deserialize<T>("{}", options)!;
    }
}
