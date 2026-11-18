using System.Security.Claims;
using Wms.Api.Infrastructure;
using Wms.Application.Auth;
using Wms.Application.Demo;
using Microsoft.Extensions.Options;

namespace Wms.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest body, LoginHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(body, ct)))
            .RequireRateLimiting(RateLimitingSetup.AuthPolicy);

        group.MapPost("/register", async (RegisterRequest body, RegisterHandler handler, CancellationToken ct) =>
            Results.Json(await handler.HandleAsync(body, ct), statusCode: StatusCodes.Status201Created))
            .RequireRateLimiting(RateLimitingSetup.AuthPolicy);

        group.MapGet("/demo/status", (IOptions<DemoOptions> options) =>
            Results.Ok(new DemoStatusDto(options.Value.Enabled)));

        group.MapPost("/demo", async (DemoLoginRequest body, DemoLoginHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(body, ct)))
            .RequireRateLimiting(RateLimitingSetup.DemoPolicy);

        // allowInactive в моках: /auth/me доступен и пользователю организации «на проверке»
        group.MapGet("/me", async (ClaimsPrincipal principal, GetCurrentUserHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(principal.FindFirstValue("sub")!, DemoClaims.IsDemo(principal), ct)))
            .RequireAuthorization();

        return group;
    }
}
