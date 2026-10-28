using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;
using Wms.Domain.Services;

namespace Wms.Application.Tasks;

/// <summary>Данные склада для листов отбора: остатки, ячейки и служебные зоны (PCK/PAK).</summary>
public sealed class PickContext
{
    private readonly Dictionary<(string WarehouseId, string Zone), string?> _firstInZone;

    private PickContext(List<StockSlot> stock, Dictionary<string, StorageLocation> locations, Dictionary<(string, string), string?> firstInZone)
    {
        Stock = stock;
        Locations = locations;
        _firstInZone = firstInZone;
    }

    public IReadOnlyList<StockSlot> Stock { get; }
    public IReadOnlyDictionary<string, StorageLocation> Locations { get; }

    public static async Task<PickContext> LoadAsync(IAppDbContext db, IReadOnlyCollection<string> warehouseIds, CancellationToken ct)
    {
        var stock = await db.Stock.AsNoTracking().Where(s => warehouseIds.Contains(s.WarehouseId))
            .OrderBy(s => EF.Property<long>(s, "Seq"))
            .Select(s => new StockSlot(s.ProductId, s.WarehouseId, s.LocationId, s.Quantity)).ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking().Where(l => warehouseIds.Contains(l.WarehouseId))
            .OrderBy(l => EF.Property<long>(l, "Seq")).ToListAsync(ct);
        var zones = await db.Zones.AsNoTracking().Where(z => warehouseIds.Contains(z.WarehouseId) && (z.Code == "PCK" || z.Code == "PAK"))
            .OrderBy(z => EF.Property<long>(z, "Seq")).ToListAsync(ct);
        var first = new Dictionary<(string, string), string?>();
        foreach (var z in zones)
        {
            if (!first.ContainsKey((z.WarehouseId, z.Code))) first[(z.WarehouseId, z.Code)] = locations.FirstOrDefault(l => l.ZoneId == z.Id)?.Id;
        }
        return new PickContext(stock, locations.ToDictionary(l => l.Id), first);
    }

    public string? FirstLocation(string warehouseId, string zoneCode) => _firstInZone.GetValueOrDefault((warehouseId, zoneCode));

    public List<TaskItem> PickItems(IEnumerable<Order> orders) => PickListBuilder.Build(orders, Stock, wh => FirstLocation(wh, "PCK"));

    public IReadOnlyDictionary<string, RouteNode> Nodes => Locations.ToDictionary(kv => kv.Key, kv => new RouteNode(kv.Value.Code, kv.Value.X, kv.Value.Y));
}
