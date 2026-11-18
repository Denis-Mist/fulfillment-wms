using Wms.Application.Demo;

namespace Wms.Api.Infrastructure;

/// <summary>
/// Во время сброса — 503 на /api (кроме GET /api/auth/demo/status); /health не под /api и всегда 200.
/// Для demo-токена применяет стоп-лист мутаций.
/// </summary>
public sealed class DemoGuardMiddleware(RequestDelegate next)
{
    public const int ResetRetryAfterSeconds = 60;
    public const string ResetTitle = "Демо-данные обновляются, подождите минуту";

    public async Task InvokeAsync(HttpContext context, IDemoResetGate gate)
    {
        var path = context.Request.Path;
        if (gate.IsResetting && path.StartsWithSegments("/api") && !IsDemoStatus(context))
        {
            context.Response.Headers.RetryAfter = ResetRetryAfterSeconds.ToString();
            await Problems.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, ResetTitle);
            return;
        }

        if (DemoClaims.IsDemo(context.User) && DemoStopList.IsBlocked(context.Request.Method, path.Value))
        {
            await Problems.WriteAsync(context, StatusCodes.Status403Forbidden, DemoStopList.ForbiddenTitle);
            return;
        }

        await next(context);
    }

    private static bool IsDemoStatus(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.Equals("/api/auth/demo/status", StringComparison.OrdinalIgnoreCase);
}
