using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Catalog;
using Wms.Domain.Enums;
using Wms.Infrastructure.Persistence;

namespace Wms.Infrastructure.Operational;

/// <summary>Метрики из БД: заказы, товары, остатки и поставки (формулы — как в моках adminPlatform/catalog).</summary>
internal sealed class OperationalMetrics(WmsDbContext db) : IOperationalMetrics, IProductSalesSource
{
    public async Task<PlatformOrderStats> GetPlatformStatsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-30);
        var prevSince = now.AddDays(-60);
        var inbound30d = await db.InboundShipments.CountAsync(s => s.CreatedAt >= since, ct);
        var ordersPrev = await db.Orders.CountAsync(o => o.CreatedAt >= prevSince && o.CreatedAt < since, ct);
        var recent = await db.Orders.AsNoTracking().Where(o => o.CreatedAt >= since)
            .Select(o => new { o.CreatedAt, o.Channel, o.WarehouseId, o.ShippedAt, o.ShipBy }).ToListAsync(ct);
        var shipped = recent.Where(o => o.ShippedAt != null).ToList();
        var byDay = recent.GroupBy(o => DateOnly.FromDateTime(o.CreatedAt.UtcDateTime)).ToDictionary(g => g.Key, g => new ChannelCounts(
            g.Count(o => o.Channel == SalesChannel.Wb), g.Count(o => o.Channel == SalesChannel.Ozon), g.Count(o => o.Channel == SalesChannel.Own)));
        var byWarehouse = recent.GroupBy(o => o.WarehouseId).ToDictionary(g => g.Key, g => g.Count());
        return new PlatformOrderStats(recent.Count, ordersPrev, shipped.Count, shipped.Count(o => o.ShippedAt <= o.ShipBy), inbound30d, byDay, byWarehouse);
    }

    public Task<int> CountOrders30dAsync(DateTimeOffset now, string? sellerId, IReadOnlyCollection<string>? warehouseIds, CancellationToken ct)
    {
        var since = now.AddDays(-30);
        var query = db.Orders.Where(o => o.CreatedAt >= since);
        if (sellerId is not null) query = query.Where(o => o.SellerId == sellerId);
        if (warehouseIds is not null) query = query.Where(o => warehouseIds.Contains(o.WarehouseId));
        return query.CountAsync(ct);
    }

    public Task<int> CountProductsAsync(string sellerId, CancellationToken ct) =>
        db.Products.CountAsync(p => p.SellerId == sellerId, ct);

    public Task<int> StockUnitsAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct) =>
        db.Stock.Where(s => s.SellerId == sellerId && warehouseIds.Contains(s.WarehouseId)).SumAsync(s => s.Quantity, ct);

    /// <summary>Продано за 30 дней: неотменённые заказы на складах (null — на всех).</summary>
    public async Task<int> Sold30dAsync(string productId, IReadOnlyCollection<string>? warehouseIds, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-30);
        var query = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled);
        if (warehouseIds is not null) query = query.Where(o => warehouseIds.Contains(o.WarehouseId));
        return (await query.ToListAsync(ct)).Sum(o => o.QuantityOf(productId));
    }

    public async Task<IReadOnlyList<int>> OrdersTrendAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, IReadOnlyList<DateTimeOffset> dayStarts, CancellationToken ct)
    {
        if (dayStarts.Count == 0) return [];
        var from = dayStarts.Min();
        var to = dayStarts.Max().AddDays(1);
        var created = await db.Orders.Where(o => o.SellerId == sellerId && warehouseIds.Contains(o.WarehouseId) && o.CreatedAt >= from && o.CreatedAt < to)
            .Select(o => o.CreatedAt).ToListAsync(ct);
        return dayStarts.Select(d => created.Count(c => c >= d && c < d.AddDays(1))).ToList();
    }

    public Task<int> ActiveInboundAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct) =>
        db.InboundShipments.CountAsync(s => s.SellerId == sellerId && warehouseIds.Contains(s.WarehouseId) &&
            (s.Status == InboundStatus.InTransit || s.Status == InboundStatus.Arrived || s.Status == InboundStatus.Receiving), ct);
}

/// <summary>Временный пароль формата моков: wms-xxxxxx.</summary>
internal sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    public string Generate() => "wms-" + RandomNumberGenerator.GetString(Alphabet, 6);
}
