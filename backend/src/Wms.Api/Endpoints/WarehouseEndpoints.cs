using Wms.Api.Infrastructure;
using Wms.Application.Catalog;
using Wms.Application.Warehouses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Api.Endpoints;

/// <summary>Склады (зоны, ячейки, карта, проверка размещения) и каталог товаров.</summary>
public static class WarehouseEndpoints
{
    public static RouteGroupBuilder MapWarehouseEndpoints(this RouteGroupBuilder api)
    {
        var wh = api.MapGroup("/warehouses").WithTags("Warehouses");
        wh.MapGet("", async (HttpContext http, WarehouseListHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), ct))).RequireAccess();
        wh.MapGet("/{id}/zones", async (HttpContext http, string id, ZonesQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, ct))).RequireAccess();
        wh.MapPost("/{id}/zones", async (HttpContext http, string id, CreateZoneHandler h, CancellationToken ct) =>
            Results.Json(await h.HandleAsync(http.Ctx(), id, await Body.ReadAsync<ZoneInput>(http, ct), ct),
                statusCode: StatusCodes.Status201Created)).RequireAccess(WarehouseRoles.Management);
        wh.MapGet("/{id}/locations", async (HttpContext http, string id, LocationsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, new LocationQuery(ListQuery.Page(http.Request),
                ListQuery.String(http.Request, "zoneId"), ListQuery.String(http.Request, "occupancy")), ct))).RequireAccess();
        wh.MapPost("/{id}/locations", async (HttpContext http, string id, CreateLocationHandler h, CancellationToken ct) =>
            Results.Json(await h.HandleAsync(http.Ctx(), id, await Body.ReadAsync<LocationInput>(http, ct), ct),
                statusCode: StatusCodes.Status201Created)).RequireAccess(WarehouseRoles.Management);
        wh.MapGet("/{id}/map", async (HttpContext http, string id, WarehouseMapQueryHandler h, CancellationToken ct) =>
        {
            var raw = http.Request.Query["level"].FirstOrDefault();
            var level = raw is null ? 1 : int.TryParse(raw, out var v) ? v : 0; // Number('x') = NaN → пустая карта
            return Results.Ok(await h.HandleAsync(http.Ctx(), id, level, ct));
        }).RequireAccess();
        wh.MapGet("/{id}/products", async (HttpContext http, string id, WarehouseProductsQueryHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, http.Request.Query["search"].FirstOrDefault(), ct))).RequireAccess();
        wh.MapPost("/{id}/putaway-check", async (HttpContext http, string id, PutawayCheckHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, await Body.ReadAsync<PutawayCheckInput>(http, ct), ct))).RequireAccess();

        var sellerRoles = RoleRules.SellerRoles.ToArray();
        var catalog = api.MapGroup("/catalog/products").WithTags("Catalog");
        catalog.MapGet("/facets", async (HttpContext http, ProductFacetsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), Query(http), ct))).RequireAccess();
        catalog.MapPost("/import", async (HttpContext http, ProductCommandsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ImportAsync(http.Ctx(), await Body.ReadAsync<ImportInput>(http, ct), ct))).RequireAccess(sellerRoles);
        catalog.MapGet("", async (HttpContext http, ProductListHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), Query(http), ct))).RequireAccess();
        catalog.MapGet("/{id}", async (HttpContext http, string id, ProductDetailsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(http.Ctx(), id, ct))).RequireAccess();
        catalog.MapPost("", async (HttpContext http, ProductCommandsHandler h, CancellationToken ct) =>
            Results.Json(await h.CreateAsync(http.Ctx(), await Body.ReadAsync<ProductDraft>(http, ct), ct),
                statusCode: StatusCodes.Status201Created)).RequireAccess(sellerRoles);
        catalog.MapPut("/{id}", async (HttpContext http, string id, ProductCommandsHandler h, CancellationToken ct) =>
            Results.Ok(await h.UpdateAsync(http.Ctx(), id, await Body.ReadAsync<ProductDraft>(http, ct), ct))).RequireAccess(sellerRoles);
        catalog.MapPost("/{id}/{action}", async (HttpContext http, string id, string action, ProductCommandsHandler h, CancellationToken ct) =>
            Results.Ok(await h.ActAsync(http.Ctx(), id, action, ct))).RequireAccess(sellerRoles);
        return api;
    }

    private static ProductQuery Query(HttpContext http) => new(ListQuery.Page(http.Request),
        ListQuery.String(http.Request, "sellerId"), ListQuery.String(http.Request, "category"), ListQuery.String(http.Request, "flag"));
}
