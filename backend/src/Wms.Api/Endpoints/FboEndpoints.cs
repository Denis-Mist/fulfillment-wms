using Wms.Api.Infrastructure;
using Wms.Application.Fbo;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>FBO-отгрузки на склады маркетплейсов (/fbo).</summary>
public static class FboEndpoints
{
    private static readonly Role[] Sellers = [Role.SellerOwner, Role.SellerManager];
    private static readonly Role[] FcOps = [Role.FcOwner, Role.FcManager, Role.FcSupervisor];

    public sealed record AdvanceBody(FboStatus? To);

    public static RouteGroupBuilder MapFboEndpoints(this RouteGroupBuilder api)
    {
        var f = api.MapGroup("/fbo").WithTags("FBO");
        f.MapGet("/summary", async (HttpContext http, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), ListQuery.String(http.Request, "warehouseId"), ct))).RequireAccess();
        f.MapGet("/stock-options", async (HttpContext http, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.StockOptionsAsync(http.Ctx(), ct))).RequireAccess();
        f.MapGet("", async (HttpContext http, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), new FboQuery(ListQuery.Page(http.Request), ListQuery.Enum<FboStatus>(http.Request, "status"),
                ListQuery.Enum<FboMarketplace>(http.Request, "marketplace"), ListQuery.String(http.Request, "sellerId"),
                ListQuery.String(http.Request, "warehouseId")), ct))).RequireAccess();
        f.MapGet("/{id}", async (HttpContext http, string id, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.DetailsAsync(http.Ctx(), id, ct))).RequireAccess();
        f.MapPost("", async (HttpContext http, FboHandler h, CancellationToken ct) =>
        {
            var body = await Body.ReadOrEmptyAsync<FboInput>(http, ct);
            var created = await h.CreateAsync(http.Ctx(), body, ct);
            return Results.Created($"/api/fbo/{created.Id}", created);
        }).RequireAccess(Sellers);
        f.MapPost("/{id}/submit", async (HttpContext http, string id, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.SubmitAsync(http.Ctx(), id, ct))).RequireAccess(Sellers);
        f.MapPost("/{id}/cancel", async (HttpContext http, string id, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.CancelAsync(http.Ctx(), id, ct))).RequireAccess(Sellers);
        f.MapPost("/{id}/advance", async (HttpContext http, string id, FboHandler h, CancellationToken ct) =>
            Results.Ok(await h.AdvanceAsync(http.Ctx(), id, (await Body.ReadOrEmptyAsync<AdvanceBody>(http, ct)).To, ct))).RequireAccess(FcOps);
        return api;
    }
}
