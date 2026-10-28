using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;

namespace Wms.Application.Warehouses;

/// <summary>GET /warehouses — склады организации с числом ячеек и занятых ячеек.</summary>
public sealed class WarehouseListHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<IReadOnlyList<WarehouseDto>> HandleAsync(RequestContext ctx, CancellationToken ct)
    {
        var visible = await scope.VisibleWarehouseIdsAsync(ctx, ct);
        var warehouses = (await db.Warehouses.AsNoTracking().OrderBy(w => EF.Property<long>(w, "Seq")).ToListAsync(ct))
            .Where(w => visible is null || visible.Contains(w.Id)).ToList();
        var ids = warehouses.Select(w => w.Id).ToList();
        var totals = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.WarehouseId))
            .GroupBy(l => l.WarehouseId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var occupied = await db.Stock.AsNoTracking().Where(s => ids.Contains(s.WarehouseId) && s.Quantity > 0)
            .GroupBy(s => s.WarehouseId).Select(g => new { g.Key, Count = g.Select(s => s.LocationId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return warehouses.Select(w => new WarehouseDto(w.Id, w.OrganizationId, w.Name, w.Code, w.City, w.Address, w.AreaSqm, w.IsActive,
            totals.GetValueOrDefault(w.Id), occupied.GetValueOrDefault(w.Id))).ToList();
    }
}

public sealed class ZonesQueryHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<IReadOnlyList<ZoneDto>> HandleAsync(RequestContext ctx, string warehouseId, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        var stock = await WarehouseStock.LoadAsync(db, warehouse.Id, ct);
        var zones = await db.Zones.AsNoTracking().Where(z => z.WarehouseId == warehouse.Id).OrderBy(z => EF.Property<long>(z, "Seq")).ToListAsync(ct);
        var locations = (await db.Locations.AsNoTracking().Where(l => l.WarehouseId == warehouse.Id).ToListAsync(ct))
            .ToLookup(l => l.ZoneId);
        return zones.Select(z => WarehouseMapper.ToDto(z, locations[z.Id].ToList(), stock)).ToList();
    }
}

public sealed record LocationQuery(PageQuery Page, string? ZoneId, string? Occupancy);

public sealed class LocationsQueryHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<Paged<StorageLocationDto>> HandleAsync(RequestContext ctx, string warehouseId, LocationQuery query, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        var stock = await WarehouseStock.LoadAsync(db, warehouse.Id, ct);
        var zoneCodes = await db.Zones.AsNoTracking().ToDictionaryAsync(z => z.Id, z => z.Code, ct);
        var orgNames = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var term = query.Page.Term;
        var locations = await db.Locations.AsNoTracking().Where(l => l.WarehouseId == warehouse.Id)
            .OrderBy(l => EF.Property<long>(l, "Seq")).ToListAsync(ct);
        var items = locations
            .Where(l => string.IsNullOrEmpty(query.ZoneId) || l.ZoneId == query.ZoneId)
            .Where(l => term.Length == 0 || l.Code.ToLowerInvariant().Contains(term))
            .Select(l => WarehouseMapper.ToDto(l, zoneCodes.GetValueOrDefault(l.ZoneId) ?? "—", stock, orgNames))
            .Where(l => MatchesOccupancy(l, query.Occupancy))
            .SortBy(SortKey(query.Page.SortField ?? "code"), query.Page.OrderOr(SortOrder.Asc));
        return Paged<StorageLocationDto>.From(items, query.Page);
    }

    /// <summary>Как в моках: empty — без товара, full — заполнение ≥ 90 %, любое другое значение — частично заполненные.</summary>
    private static bool MatchesOccupancy(StorageLocationDto l, string? occupancy)
    {
        if (string.IsNullOrEmpty(occupancy)) return true;
        var ratio = l.UsedLiters / l.CapacityLiters;
        return occupancy switch
        {
            "empty" => l.Quantity == 0,
            "full" => ratio >= 0.9,
            _ => l.Quantity > 0 && ratio < 0.9,
        };
    }

    private static Func<StorageLocationDto, object?>? SortKey(string field) => field switch
    {
        "id" => l => l.Id,
        "code" => l => l.Code,
        "zoneId" => l => l.ZoneId,
        "zoneCode" => l => l.ZoneCode,
        "aisle" => l => l.Aisle,
        "rack" => l => l.Rack,
        "x" => l => l.X,
        "y" => l => l.Y,
        "level" => l => l.Level,
        "capacityLiters" => l => l.CapacityLiters,
        "usedLiters" => l => l.UsedLiters,
        "skuCount" => l => l.SkuCount,
        "quantity" => l => l.Quantity,
        "warehouseId" => l => l.WarehouseId,
        _ => null,
    };
}

public sealed class WarehouseMapQueryHandler(IAppDbContext db, WarehouseScope scope)
{
    public async Task<IReadOnlyList<WarehouseMapCellDto>> HandleAsync(RequestContext ctx, string warehouseId, int level, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        var stock = await WarehouseStock.LoadAsync(db, warehouse.Id, ct);
        var locations = await db.Locations.AsNoTracking().Where(l => l.WarehouseId == warehouse.Id && l.Level == level)
            .OrderBy(l => EF.Property<long>(l, "Seq")).ToListAsync(ct);
        return locations.Select(l => new WarehouseMapCellDto(l.Id, l.Code, l.Aisle, l.Rack,
            Math.Min(1, stock.UsedLiters(l.Id) / l.CapacityLiters))).ToList();
    }
}

public sealed record WarehouseProductOption(string Id, string Sku, string Name, double VolumeLiters, double WeightG, string SellerName);

/// <summary>Товары клиентов склада для подбора при размещении (до 30 позиций).</summary>
public sealed class WarehouseProductsQueryHandler(IAppDbContext db, WarehouseScope scope)
{
    public const int Limit = 30;

    public async Task<IReadOnlyList<WarehouseProductOption>> HandleAsync(RequestContext ctx, string warehouseId, string? search, CancellationToken ct)
    {
        var warehouse = await scope.RequireWarehouseAsync(ctx, warehouseId, ct);
        var term = (search ?? string.Empty).Trim().ToLowerInvariant();
        var sellers = (await db.Contracts.AsNoTracking().ToListAsync(ct))
            .Where(c => c.WarehouseIds.Contains(warehouse.Id)).Select(c => c.SellerId).ToHashSet();
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var products = await db.Products.AsNoTracking().Where(p => sellers.Contains(p.SellerId))
            .OrderBy(p => EF.Property<long>(p, "Seq")).ToListAsync(ct);
        return products
            .Where(p => term.Length == 0 || p.Name.ToLowerInvariant().Contains(term) || p.Sku.ToLowerInvariant().Contains(term) || p.Barcode.Contains(term))
            .Take(Limit)
            .Select(p => new WarehouseProductOption(p.Id, p.Sku, p.Name, p.VolumeLiters, p.WeightG, names.GetValueOrDefault(p.SellerId) ?? "—"))
            .ToList();
    }
}
