using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Stock;

public sealed record StockItemDto(string Id, string ProductId, string Sku, string ProductName, string Barcode, string SellerId, string SellerName,
    string WarehouseId, string WarehouseName, string LocationId, string LocationCode, string ZoneCode, int Quantity, int Reserved,
    int Available, StockStatus Status, double VolumeLiters);

public sealed record StockSummary(int SkuCount, int Quantity, int Reserved, int Available, int Defect, int EmptyLocations);

public sealed record ProductWarehouseStock(string WarehouseId, string WarehouseName, string FulfillmentName, int Available, int Reserved);

public sealed record ProductStockDto(string Id, string ProductId, string Sku, string Name, int Available, int Reserved, int Defect,
    double AvgDailySales, int? DaysOfCover, IReadOnlyList<ProductWarehouseStock> Warehouses);

public sealed record StockLocationOption(string Id, string Code, string ZoneCode, ZoneType ZoneType);

public sealed record StockQuery(PageQuery Page, string? WarehouseId, string? SellerId, string? Zone, StockStatus? Status);

/// <summary>Остатки, видимые организации, со справочниками для отображения (как toStockItem() в моках).</summary>
public sealed class StockReadModel(IAppDbContext db, WarehouseScope warehouses)
{
    public sealed record View(StockItem Row, StockItemDto Dto);

    public async Task<IReadOnlyList<View>> VisibleAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var query = db.Stock.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(s => s.WarehouseId == warehouseId);
        if (ctx.IsSeller) query = query.Where(s => s.SellerId == ctx.OrganizationId);
        else if (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible) query = query.Where(s => visible.Contains(s.WarehouseId));
        var rows = await query.OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
        var lookups = await LoadLookupsAsync(rows.Select(r => r.WarehouseId).Distinct().ToList(), ct);
        return rows.Select(r => new View(r, lookups.ToDto(r))).ToList();
    }

    public async Task<bool> CanSeeAsync(RequestContext ctx, StockItem s, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return true;
        if (ctx.IsSeller) return s.SellerId == ctx.OrganizationId;
        return (await warehouses.VisibleWarehouseIdsAsync(ctx, ct))?.Contains(s.WarehouseId) ?? true;
    }

    public async Task<StockItemDto> ToDtoAsync(StockItem row, CancellationToken ct) => (await LoadLookupsAsync([row.WarehouseId], ct)).ToDto(row);

    internal async Task<StockLookups> LoadLookupsAsync(IReadOnlyCollection<string> warehouseIds, CancellationToken ct) => new(
        await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct),
        await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, ct),
        await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct),
        await db.Locations.AsNoTracking().Where(l => warehouseIds.Contains(l.WarehouseId)).ToDictionaryAsync(l => l.Id, ct),
        await db.Zones.AsNoTracking().Where(z => warehouseIds.Contains(z.WarehouseId)).ToDictionaryAsync(z => z.Id, ct));
}

internal sealed record StockLookups(IReadOnlyDictionary<string, string> OrgNames, IReadOnlyDictionary<string, Warehouse> Warehouses,
    IReadOnlyDictionary<string, Product> Products, IReadOnlyDictionary<string, StorageLocation> Locations, IReadOnlyDictionary<string, Zone> Zones)
{
    public Zone? ZoneOf(StockItem s) => Locations.GetValueOrDefault(s.LocationId) is { } l ? Zones.GetValueOrDefault(l.ZoneId) : null;

    public StockItemDto ToDto(StockItem s)
    {
        var product = Products.GetValueOrDefault(s.ProductId);
        var location = Locations.GetValueOrDefault(s.LocationId);
        var zone = ZoneOf(s);
        return new StockItemDto(s.Id, s.ProductId, product?.Sku ?? "—", product?.Name ?? "Товар", product?.Barcode ?? string.Empty,
            s.SellerId, OrgNames.GetValueOrDefault(s.SellerId) ?? "—", s.WarehouseId, Warehouses.GetValueOrDefault(s.WarehouseId)?.Name ?? "—",
            s.LocationId, location?.Code ?? "—", zone?.Code ?? "—", s.Quantity, s.Reserved, s.AvailableIn(zone?.Type), s.StatusIn(zone?.Type),
            JsMath.Round1((product?.VolumeLiters ?? 0) * s.Quantity));
    }
}

public sealed class StockQueryHandler(IAppDbContext db, StockReadModel stock, WarehouseScope warehouses, IClock clock)
{
    public async Task<Paged<StockItemDto>> ListAsync(RequestContext ctx, StockQuery q, CancellationToken ct)
    {
        var term = q.Page.Term;
        var items = (await stock.VisibleAsync(ctx, q.WarehouseId, ct)).Select(v => v.Dto)
            .Where(i => string.IsNullOrEmpty(q.SellerId) || i.SellerId == q.SellerId)
            .Where(i => string.IsNullOrEmpty(q.Zone) || i.ZoneCode == q.Zone)
            .Where(i => q.Status switch
            {
                null => true,
                StockStatus.Reserved => i.Reserved > 0 && i.Status != StockStatus.Defect,
                var s => i.Status == s,
            })
            .Where(i => term.Length == 0 || new[] { i.Sku, i.ProductName, i.Barcode, i.LocationCode, i.SellerName }
                .Any(v => v.ToLowerInvariant().Contains(term)));
        return Paged<StockItemDto>.From(items.SortBy(SortKey(q.Page.SortField ?? "locationCode"), q.Page.OrderOr(SortOrder.Asc)), q.Page);
    }

    public async Task<StockSummary> SummaryAsync(RequestContext ctx, string? warehouseId, string? sellerId, CancellationToken ct)
    {
        var items = (await stock.VisibleAsync(ctx, warehouseId, ct)).Select(v => v.Dto)
            .Where(i => string.IsNullOrEmpty(sellerId) || i.SellerId == sellerId).ToList();
        var used = items.Select(i => i.LocationId).ToHashSet();
        var visible = await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        var locations = db.Locations.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) locations = locations.Where(l => l.WarehouseId == warehouseId);
        if (visible is not null) locations = locations.Where(l => visible.Contains(l.WarehouseId));
        var empty = (await locations.Select(l => l.Id).ToListAsync(ct)).Count(id => !used.Contains(id));
        return new StockSummary(items.Select(i => i.ProductId).Distinct().Count(), items.Sum(i => i.Quantity), items.Sum(i => i.Reserved),
            items.Sum(i => i.Available), items.Where(i => i.Status == StockStatus.Defect).Sum(i => i.Quantity), empty);
    }

    /// <summary>Остатки по товарам с запасом в днях; продажи — из заказов за 30 дней (пока заказов нет — 0).</summary>
    public async Task<Paged<ProductStockDto>> ProductsAsync(RequestContext ctx, PageQuery page, bool lowStock, CancellationToken ct)
    {
        var views = await stock.VisibleAsync(ctx, null, ct);
        var lookups = await stock.LoadLookupsAsync(views.Select(v => v.Row.WarehouseId).Distinct().ToList(), ct);
        var sales = await SalesAsync(ctx, clock.UtcNow, ct);
        var byProduct = new Dictionary<string, (StockItemDto First, int Available, int Reserved, int Defect, List<ProductWarehouseStock> Warehouses)>();
        var order = new List<string>();
        foreach (var (row, item) in views)
        {
            if (!byProduct.TryGetValue(row.ProductId, out var entry))
            {
                entry = (item, 0, 0, 0, []);
                order.Add(row.ProductId);
            }
            if (item.Status == StockStatus.Defect) entry.Defect += row.Quantity;
            else { entry.Available += item.Available; entry.Reserved += row.Reserved; }
            var index = entry.Warehouses.FindIndex(w => w.WarehouseId == row.WarehouseId);
            if (index < 0)
            {
                var warehouse = lookups.Warehouses.GetValueOrDefault(row.WarehouseId);
                entry.Warehouses.Add(new ProductWarehouseStock(row.WarehouseId, item.WarehouseName,
                    lookups.OrgNames.GetValueOrDefault(warehouse?.OrganizationId ?? string.Empty) ?? "—", 0, 0));
                index = entry.Warehouses.Count - 1;
            }
            var wh = entry.Warehouses[index];
            entry.Warehouses[index] = wh with { Available = wh.Available + item.Available, Reserved = wh.Reserved + row.Reserved };
            byProduct[row.ProductId] = entry;
        }

        var term = page.Term;
        var items = order.Select(id =>
            {
                var e = byProduct[id];
                var avg = JsMath.Round1(sales.GetValueOrDefault(id) / 30.0);
                return new ProductStockDto(id, id, e.First.Sku, e.First.ProductName, e.Available, e.Reserved, e.Defect, avg,
                    avg > 0 ? (int)Math.Floor(e.Available / avg) : null, e.Warehouses);
            })
            .Where(p => term.Length == 0 || p.Name.ToLowerInvariant().Contains(term) || p.Sku.ToLowerInvariant().Contains(term))
            .Where(p => !lowStock || p.DaysOfCover is < 14);
        var sortOrder = page.OrderOr(SortOrder.Asc);
        var field = page.SortField ?? "daysOfCover";
        // null (нет продаж) — в конец при сортировке по запасу, как в моках
        Func<ProductStockDto, object?>? key = field == "daysOfCover"
            ? p => p.DaysOfCover ?? (sortOrder == SortOrder.Asc ? 1e9 : -1)
            : ProductSortKey(field);
        return Paged<ProductStockDto>.From(items.SortBy(key, sortOrder), page);
    }

    public async Task<IReadOnlyList<StockLocationOption>> LocationsAsync(RequestContext ctx, string? warehouseId, string? search, CancellationToken ct)
    {
        var visible = await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        if (string.IsNullOrEmpty(warehouseId) || (visible is not null && !visible.Contains(warehouseId))) throw AppException.NotFound("Склад не найден");
        var term = (search ?? string.Empty).ToLowerInvariant();
        var zones = await db.Zones.AsNoTracking().Where(z => z.WarehouseId == warehouseId).ToDictionaryAsync(z => z.Id, ct);
        var locations = await db.Locations.AsNoTracking().Where(l => l.WarehouseId == warehouseId)
            .OrderBy(l => EF.Property<long>(l, "Seq")).ToListAsync(ct);
        return locations.Where(l => term.Length == 0 || l.Code.ToLowerInvariant().Contains(term)).Take(30)
            .Select(l => new StockLocationOption(l.Id, l.Code, zones.GetValueOrDefault(l.ZoneId)?.Code ?? string.Empty,
                zones.GetValueOrDefault(l.ZoneId)?.Type ?? ZoneType.Storage)).ToList();
    }

    public async Task<StockItemDto> DetailsAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var row = await db.Stock.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null || !await stock.CanSeeAsync(ctx, row, ct)) throw AppException.NotFound("Позиция остатка не найдена");
        return await stock.ToDtoAsync(row, ct);
    }

    /// <summary>Порт продаж: сейчас заказов в бэкенде нет, поэтому продажи пустые (avgDailySales = 0, daysOfCover = null).</summary>
    private static Task<Dictionary<string, int>> SalesAsync(RequestContext ctx, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(new Dictionary<string, int>());

    private static Func<StockItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => i => i.Id, "productId" => i => i.ProductId, "sku" => i => i.Sku, "productName" => i => i.ProductName,
        "barcode" => i => i.Barcode, "sellerId" => i => i.SellerId, "sellerName" => i => i.SellerName,
        "warehouseId" => i => i.WarehouseId, "warehouseName" => i => i.WarehouseName, "locationId" => i => i.LocationId,
        "locationCode" => i => i.LocationCode, "zoneCode" => i => i.ZoneCode, "quantity" => i => i.Quantity,
        "reserved" => i => i.Reserved, "available" => i => i.Available, "status" => i => SnakeCaseName.Of(i.Status),
        "volumeLiters" => i => i.VolumeLiters,
        _ => null,
    };

    private static Func<ProductStockDto, object?>? ProductSortKey(string field) => field switch
    {
        "id" => p => p.Id, "productId" => p => p.ProductId, "sku" => p => p.Sku, "name" => p => p.Name,
        "available" => p => p.Available, "reserved" => p => p.Reserved, "defect" => p => p.Defect,
        "avgDailySales" => p => p.AvgDailySales,
        _ => null,
    };
}
