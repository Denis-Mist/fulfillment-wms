using Wms.Api.Infrastructure;
using Wms.Application.Admin;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAccess(Role.PlatformAdmin);

        admin.MapGet("/organizations/summary", async (HttpRequest req, AdminOrganizationsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(ListQuery.Page(req), ListQuery.Enum<OrganizationType>(req, "type"), ct)));
        admin.MapGet("/organizations", async (HttpRequest req, AdminOrganizationsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(new AdminOrganizationQuery(ListQuery.Page(req),
                ListQuery.Enum<OrganizationType>(req, "type"), ListQuery.Enum<OrganizationStatus>(req, "status")), ct)));
        admin.MapGet("/organizations/{id}", async (string id, OrganizationDetailsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(id, ct)));
        admin.MapPost("/organizations/{id}/{action}", async (string id, string action, ModerateOrganizationHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(id, action, ct)));

        admin.MapGet("/users", async (HttpRequest req, PlatformUsersQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new PlatformUserQuery(ListQuery.Page(req), ListQuery.Enum<Role>(req, "role"),
                ListQuery.String(req, "state")), ct)));
        admin.MapPost("/users/{id}/{action}", async (HttpContext http, string id, string action, PlatformUserActionHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, action, ct)));

        admin.MapGet("/audit", async (HttpRequest req, AuditQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new AuditQuery(ListQuery.Page(req), ListQuery.Enum<AuditCategory>(req, "category"),
                ListQuery.String(req, "organizationId")), ct)));

        admin.MapGet("/references", async (HttpRequest req, ReferencesQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(ListQuery.Enum<ReferenceKind>(req, "kind"), ct)));
        admin.MapPost("/references", async (HttpContext http, ReferenceInput body, CreateReferenceHandler h, CancellationToken ct) =>
            Results.Json(await h.HandleAsync(http.Ctx(), body, ct), statusCode: StatusCodes.Status201Created));
        admin.MapPost("/references/{id}/toggle", async (HttpContext http, string id, ToggleReferenceHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, ct)));

        admin.MapGet("/overview", async (PlatformOverviewHandler h, CancellationToken ct) => Results.Ok(await h.HandleAsync(ct)));
        return api;
    }
}
