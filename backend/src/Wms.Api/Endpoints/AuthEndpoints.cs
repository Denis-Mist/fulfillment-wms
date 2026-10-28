using System.Security.Claims;
using Wms.Api.Infrastructure;
using Wms.Application.Auth;

namespace Wms.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest body, LoginHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(body, ct)));

        group.MapPost("/register", async (RegisterRequest body, RegisterHandler handler, CancellationToken ct) =>
            Results.Json(await handler.HandleAsync(body, ct), statusCode: StatusCodes.Status201Created));

        // allowInactive в моках: /auth/me доступен и пользователю организации «на проверке»
        group.MapGet("/me", async (ClaimsPrincipal principal, GetCurrentUserHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(principal.FindFirstValue("sub")!, ct)))
            .RequireAuthorization();

        return group;
    }
}
