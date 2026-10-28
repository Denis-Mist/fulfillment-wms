using System.Text.Json;
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
            return await JsonSerializer.DeserializeAsync<T>(http.Request.Body, options, ct);
        }
        catch (JsonException)
        {
            throw AppException.BadRequest("Некорректный запрос", "Тело запроса не является корректным JSON");
        }
    }
}
