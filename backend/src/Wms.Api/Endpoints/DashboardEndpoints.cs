using Wms.Api.Infrastructure;
using Wms.Application.Dashboard;

namespace Wms.Api.Endpoints;

/// <summary>Дашборды кабинетов (/dashboard).</summary>
public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/dashboard").WithTags("Dashboard");
        g.MapGet("/fulfillment", async (HttpContext http, DashboardHandler h, CancellationToken ct) =>
            Results.Ok(await h.FulfillmentAsync(http.Ctx(), ListQuery.String(http.Request, "period"), ListQuery.String(http.Request, "warehouseId"), ct))).RequireAccess();
        g.MapGet("/seller", async (HttpContext http, DashboardHandler h, CancellationToken ct) =>
            Results.Ok(await h.SellerAsync(http.Ctx(), ListQuery.String(http.Request, "period"), ct))).RequireAccess();
        return api;
    }
}
