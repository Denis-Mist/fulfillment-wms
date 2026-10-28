using Wms.Api.Infrastructure;
using Wms.Application.Orders;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Заказы: список, сводка и карточка.</summary>
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/orders").WithTags("Orders");
        g.MapGet("/summary", async (HttpContext http, OrderQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), Query(http), ct))).RequireAccess();
        g.MapGet("", async (HttpContext http, OrderQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), Query(http), ct))).RequireAccess();
        g.MapGet("/{id}", async (HttpContext http, string id, OrderQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.DetailsAsync(http.Ctx(), id, ct))).RequireAccess();
        return api;
    }

    private static OrderQuery Query(HttpContext http) => new(ListQuery.Page(http.Request),
        ListQuery.String(http.Request, "status"), ListQuery.Enum<SalesChannel>(http.Request, "channel"),
        ListQuery.String(http.Request, "sellerId"), ListQuery.String(http.Request, "warehouseId"));
}
