using Wms.Api.Infrastructure;
using Wms.Application.Analytics;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Аналитика ФЦ (/analytics): прогноз, слоттинг, KPI — только руководство ФЦ.</summary>
public static class AnalyticsEndpoints
{
    private static readonly Role[] Management = [Role.FcOwner, Role.FcManager];

    private static int? Int(HttpRequest r, string key) => int.TryParse(r.Query[key], out var v) ? v : null;

    public static RouteGroupBuilder MapAnalyticsEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/analytics").WithTags("Analytics");
        g.MapGet("/forecast", async (HttpContext http, AnalyticsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ForecastAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), Int(http.Request, "horizon"),
                ListQuery.Enum<ForecastAlgorithm>(http.Request, "algorithm"), ct))).RequireAccess(Management);
        g.MapPost("/forecast/approve", async (HttpContext http, AnalyticsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ApproveAsync(http.Ctx(), (await Body.ReadOrEmptyAsync<ApproveInput>(http, ct)), ct))).RequireAccess(Management);
        g.MapGet("/slotting", async (HttpContext http, AnalyticsHandler h, CancellationToken ct) =>
            Results.Ok(await h.SlottingAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"),
                ListQuery.Enum<SlottingAlgorithm>(http.Request, "algorithm"), ct))).RequireAccess(Management);
        g.MapPost("/slotting/apply", async (HttpContext http, AnalyticsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ApplyAsync(await Body.ReadOrEmptyAsync<ApplyInput>(http, ct), ct))).RequireAccess(Management);
        g.MapGet("/kpi", async (HttpContext http, AnalyticsHandler h, CancellationToken ct) =>
            Results.Ok(await h.KpiAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ListQuery.String(http.Request, "sellerId"),
                Int(http.Request, "period"), ct))).RequireAccess(Management);
        return api;
    }
}
