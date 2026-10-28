using Wms.Api.Infrastructure;
using Wms.Application.Returns;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Возвраты покупателей (/returns).</summary>
public static class ReturnsEndpoints
{
    private static readonly Role[] FcOps = [Role.FcOwner, Role.FcManager, Role.FcSupervisor, Role.FcReceiver];

    public static RouteGroupBuilder MapReturnsEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/returns").WithTags("Returns");
        g.MapGet("/summary", async (HttpContext http, ReturnsHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ListQuery.String(http.Request, "sellerId"), ct))).RequireAccess();
        g.MapGet("", async (HttpContext http, ReturnsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), new ReturnQuery(ListQuery.Page(http.Request), ListQuery.Enum<ReturnStatus>(http.Request, "status"),
                ListQuery.Enum<ReturnReason>(http.Request, "reason"), ListQuery.String(http.Request, "sellerId"),
                ListQuery.String(http.Request, "warehouseId")), ct))).RequireAccess();
        g.MapGet("/{id}", async (HttpContext http, string id, ReturnsHandler h, CancellationToken ct) =>
            Results.Ok(await h.DetailsAsync(http.Ctx(), id, ct))).RequireAccess();
        g.MapPost("/{id}/receive", async (HttpContext http, string id, ReturnsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ReceiveAsync(http.Ctx(), id, ct))).RequireAccess(FcOps);
        g.MapPost("/{id}/inspect", async (HttpContext http, string id, InspectInput body, ReturnsHandler h, CancellationToken ct) =>
            Results.Ok(await h.InspectAsync(http.Ctx(), id, body, ct))).RequireAccess(FcOps);
        g.MapPost("/{id}/close", async (HttpContext http, string id, ReturnsHandler h, CancellationToken ct) =>
            Results.Ok(await h.CloseAsync(http.Ctx(), id, ct))).RequireAccess(FcOps);
        return api;
    }
}
