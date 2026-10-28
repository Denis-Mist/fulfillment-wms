using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Warehouses;

public sealed record WarehouseDto(string Id, string OrganizationId, string Name, string Code, string City, string Address,
    int AreaSqm, bool IsActive, int LocationsCount, int OccupiedLocations);

public sealed record ZoneDto(string Id, string WarehouseId, string Code, string Name, ZoneType Type,
    int LocationsCount, double CapacityLiters, double UsedLiters);

public sealed record StorageLocationDto(string Id, string WarehouseId, string ZoneId, string ZoneCode, string Code, string Aisle,
    int Rack, double X, double Y, int Level, double CapacityLiters, double UsedLiters, int SkuCount, int Quantity,
    IReadOnlyList<string> SellerNames);

public sealed record WarehouseMapCellDto(string Id, string Code, string Aisle, int Rack, double Fill);

/// <summary>Положительные остатки склада по ячейкам с объёмом единицы — общая основа зон, ячеек, карты и проверки размещения.</summary>
public sealed class WarehouseStock(IReadOnlyDictionary<string, List<WarehouseStock.Row>> byLocation)
{
    public sealed record Row(string ProductId, string SellerId, int Quantity, double UnitVolumeLiters);

    public IReadOnlyList<Row> For(string locationId) => byLocation.TryGetValue(locationId, out var rows) ? rows : [];

    public double UsedLiters(string locationId) =>
        PutawayPolicy.UsedLiters(For(locationId).Select(r => new LocationStock(r.SellerId, r.Quantity, r.UnitVolumeLiters)));

    public static async Task<WarehouseStock> LoadAsync(IAppDbContext db, string warehouseId, CancellationToken ct)
    {
        var rows = await (from s in db.Stock.AsNoTracking()
                          where s.WarehouseId == warehouseId && s.Quantity > 0
                          join p in db.Products.AsNoTracking() on s.ProductId equals p.Id into products
                          from p in products.DefaultIfEmpty()
                          orderby EF.Property<long>(s, "Seq")
                          select new { s.LocationId, s.ProductId, s.SellerId, s.Quantity, Volume = p == null ? 0 : p.VolumeLiters })
            .ToListAsync(ct);
        return new WarehouseStock(rows.GroupBy(r => r.LocationId)
            .ToDictionary(g => g.Key, g => g.Select(r => new Row(r.ProductId, r.SellerId, r.Quantity, r.Volume)).ToList()));
    }
}

/// <summary>Преобразование сущностей склада в DTO моков (toZone/toLocation/toWarehouse).</summary>
public static class WarehouseMapper
{
    public static ZoneDto ToDto(Zone zone, IReadOnlyCollection<StorageLocation> locations, WarehouseStock stock) =>
        new(zone.Id, zone.WarehouseId, zone.Code, zone.Name, zone.Type, locations.Count,
            locations.Sum(l => l.CapacityLiters), JsMath.Round(locations.Sum(l => stock.UsedLiters(l.Id))));

    public static StorageLocationDto ToDto(StorageLocation l, string zoneCode, WarehouseStock stock, IReadOnlyDictionary<string, string> orgNames)
    {
        var rows = stock.For(l.Id);
        return new StorageLocationDto(l.Id, l.WarehouseId, l.ZoneId, zoneCode, l.Code, l.Aisle, l.Rack, l.X, l.Y, l.Level,
            l.CapacityLiters, stock.UsedLiters(l.Id), rows.Select(r => r.ProductId).Distinct().Count(), rows.Sum(r => r.Quantity),
            rows.Select(r => r.SellerId).Distinct().Select(id => orgNames.GetValueOrDefault(id) ?? "—").ToList());
    }
}
