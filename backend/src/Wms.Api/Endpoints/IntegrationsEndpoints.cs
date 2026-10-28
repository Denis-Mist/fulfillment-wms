using Wms.Api.Infrastructure;
using Wms.Application.Integrations;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Интеграции селлера с маркетплейсами (/integrations).</summary>
public static class IntegrationsEndpoints
{
    private static readonly Role[] Sellers = [Role.SellerOwner, Role.SellerManager];
    private static readonly Role[] Manage = [Role.SellerOwner];

    public static RouteGroupBuilder MapIntegrationsEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/integrations").WithTags("Integrations");
        g.MapGet("", async (HttpContext http, IntegrationsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), ct))).RequireAccess(Sellers);
        g.MapPost("/{channel}/connect", async (HttpContext http, string channel, ConnectInput body, IntegrationsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ConnectAsync(http.Ctx(), channel, body, ct))).RequireAccess(Manage);
        g.MapPost("/{channel}/disconnect", async (HttpContext http, string channel, IntegrationsHandler h, CancellationToken ct) =>
            Results.Ok(await h.DisconnectAsync(http.Ctx(), channel, ct))).RequireAccess(Manage);
        g.MapPost("/{channel}/sync", async (HttpContext http, string channel, IntegrationsHandler h, CancellationToken ct) =>
            Results.Ok(await h.SyncAsync(http.Ctx(), channel, ct))).RequireAccess(Sellers);
        g.MapPut("/{channel}", async (HttpContext http, string channel, IntegrationSettingsInput body, IntegrationsHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateAsync(http.Ctx(), channel, body, ct))).RequireAccess(Manage);
        return api;
    }
}
