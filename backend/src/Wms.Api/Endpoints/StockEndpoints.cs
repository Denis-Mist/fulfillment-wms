using Wms.Api.Infrastructure;
using Wms.Application.Stock;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Остатки: список, сводка, по товарам, журнал операций, перемещение/инвентаризация/списание.</summary>
public static class StockEndpoints
{
    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/stock").WithTags("Stock");
        g.MapGet("/summary", async (HttpContext http, StockQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), Str(http, "warehouseId"), Str(http, "sellerId"), ct))).RequireAccess();
        g.MapGet("/products", async (HttpContext http, StockQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ProductsAsync(http.Ctx(), ListQuery.Page(http.Request), Str(http, "lowStock") == "true", ct))).RequireAccess();
        g.MapGet("/operations/summary", async (HttpContext http, StockOperationQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.SummaryAsync(http.Ctx(), Str(http, "warehouseId"), ct))).RequireAccess();
        g.MapGet("/operations", async (HttpContext http, StockOperationQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), new OperationQuery(ListQuery.Page(http.Request), Str(http, "warehouseId"),
                ListQuery.Enum<OperationType>(http.Request, "type"), Str(http, "stockId")), ct))).RequireAccess();
        g.MapGet("", async (HttpContext http, StockQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.ListAsync(http.Ctx(), new StockQuery(ListQuery.Page(http.Request), Str(http, "warehouseId"),
                Str(http, "sellerId"), Str(http, "zone"), ListQuery.Enum<StockStatus>(http.Request, "status")), ct))).RequireAccess();
        g.MapGet("/locations", async (HttpContext http, StockQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.LocationsAsync(http.Ctx(), Str(http, "warehouseId"), Str(http, "search"), ct))).RequireAccess();
        g.MapGet("/{id}", async (HttpContext http, string id, StockQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.DetailsAsync(http.Ctx(), id, ct))).RequireAccess();
        g.MapPost("/move", async (HttpContext http, StockCommandHandler h, CancellationToken ct) =>
            Created(await h.MoveAsync(http.Ctx(), await Body.ReadAsync<MoveInput>(http, ct), ct))).RequireAccess(StockRoles.Operations);
        g.MapPost("/inventory", async (HttpContext http, StockCommandHandler h, CancellationToken ct) =>
            Created(await h.InventoryAsync(http.Ctx(), await Body.ReadAsync<InventoryInput>(http, ct), ct))).RequireAccess(StockRoles.Operations);
        g.MapPost("/writeoff", async (HttpContext http, StockCommandHandler h, CancellationToken ct) =>
            Created(await h.WriteoffAsync(http.Ctx(), await Body.ReadAsync<WriteoffInput>(http, ct), ct))).RequireAccess(StockRoles.Operations);
        return api;
    }

    private static string? Str(HttpContext http, string key) => ListQuery.String(http.Request, key);

    private static IResult Created(StockOperationDto op) => Results.Json(op, statusCode: StatusCodes.Status201Created);
}
