using Wms.Api.Infrastructure;
using Wms.Application.Clients;
using Wms.Application.Directory;
using Wms.Application.Notifications;
using Wms.Application.Partners;
using Wms.Application.Search;
using Wms.Application.Settings;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Клиенты/партнёры, настройки, уведомления и поиск.</summary>
public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/clients", async (HttpContext http, ClientOptionsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ct))).RequireAccess();

        var fc = api.MapGroup("/fc").WithTags("Clients");
        fc.MapGet("/clients", async (HttpContext http, FcClientsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ListQuery.String(http.Request, "search"), ct))).RequireAccess();
        fc.MapGet("/clients/{id}", async (HttpContext http, string id, FcClientDetailsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, ct))).RequireAccess();
        fc.MapGet("/client-requests", async (HttpContext http, ClientRequestsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ct))).RequireAccess();
        fc.MapPost("/client-requests/{id}/{decision}", async (HttpContext http, string id, string decision,
                DecideClientRequestHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, decision, await Body.ReadAsync<DecideClientRequestInput>(http, ct), ct)))
            .RequireAccess(DecideClientRequestHandler.Roles);

        var seller = api.MapGroup("/seller/fulfillments").WithTags("Partners");
        seller.MapGet("", async (HttpContext http, FulfillmentCardsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ct))).RequireAccess([.. RoleRules.SellerRoles]);
        seller.MapPost("/{id}/connect", async (HttpContext http, string id, ConnectFulfillmentHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, await Body.ReadAsync<ConnectRequestInput>(http, ct), ct)))
            .RequireAccess(Role.SellerOwner);

        var settings = api.MapGroup("/settings").WithTags("Settings");
        settings.MapGet("", async (HttpContext http, OrgSettingsHandler h, CancellationToken ct) =>
            Results.Ok(await h.GetAsync(http.Ctx(), ct))).RequireAccess();
        settings.MapPut("/profile", async (HttpContext http, OrgSettingsHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateProfileAsync(http.Ctx(), await Body.ReadAsync<Wms.Domain.Entities.OrgProfile>(http, ct), ct)))
            .RequireAccess(SettingsRoles.Owners);
        settings.MapPut("/card-rules", async (HttpContext http, OrgSettingsHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateCardRulesAsync(http.Ctx(), await Body.ReadOrEmptyAsync<Wms.Domain.Entities.CardRules>(http, ct), ct)))
            .RequireAccess(SettingsRoles.CardRuleEditors);
        settings.MapPut("/notifications", async (HttpContext http, OrgSettingsHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateNotificationsAsync(http.Ctx(), await Body.ReadOrEmptyAsync<Wms.Domain.Entities.NotificationPrefs>(http, ct), ct)))
            .RequireAccess();
        settings.MapGet("/team", async (HttpContext http, TeamQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ct))).RequireAccess();
        settings.MapPost("/team/invite", async (HttpContext http, InviteMemberHandler h, CancellationToken ct) =>
            Results.Json(await h.HandleAsync(http.Ctx(), await Body.ReadAsync<InviteInput>(http, ct) ?? new InviteInput(null, null), ct),
                statusCode: StatusCodes.Status201Created)).RequireAccess(SettingsRoles.Owners);
        settings.MapPost("/team/{id}/revoke", async (HttpContext http, string id, RevokeInviteHandler h, CancellationToken ct) =>
        {
            await h.HandleAsync(http.Ctx(), id, ct);
            return Results.NoContent();
        }).RequireAccess(SettingsRoles.Owners);

        api.MapGet("/notifications", async (HttpContext http, NotificationsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), ct))).RequireAccess().WithTags("Notifications");
        api.MapPost("/notifications/read-all", async (HttpContext http, NotificationsHandler h, CancellationToken ct) =>
        {
            await h.MarkAllReadAsync(http.Ctx(), ct);
            return Results.NoContent();
        }).RequireAccess().WithTags("Notifications");

        api.MapGet("/search", async (HttpContext http, GlobalSearchHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ListQuery.String(http.Request, "q"), ct))).RequireAccess().WithTags("Search");
        return api;
    }
}
