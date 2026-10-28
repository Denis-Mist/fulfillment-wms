using Wms.Api.Infrastructure;
using Wms.Application.Inbound;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Поставки на ФЦ: список, сводка, карточка и переходы статусов.</summary>
public static class InboundEndpoints
{
    public static RouteGroupBuilder MapInboundEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/inbound-shipments").WithTags("Inbound");
        g.MapGet("/summary", async (HttpContext http, InboundQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), Query(http), ct))).RequireAccess();
        g.MapGet("", async (HttpContext http, InboundQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), Query(http), ct))).RequireAccess();
        g.MapGet("/{id}", async (HttpContext http, string id, InboundQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.DetailsAsync(http.Ctx(), id, ct))).RequireAccess();
        g.MapPost("/{id}/actions/{action}", async (HttpContext http, string id, string action, InboundActionHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, action, ct))).RequireAccess();
        return api;
    }

    private static InboundQuery Query(HttpContext http) => new(ListQuery.Page(http.Request),
        ListQuery.Enum<InboundStatus>(http.Request, "status"), ListQuery.String(http.Request, "sellerId"),
        ListQuery.String(http.Request, "warehouseId"));
}
