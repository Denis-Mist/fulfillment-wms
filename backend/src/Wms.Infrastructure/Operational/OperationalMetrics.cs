using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Catalog;
using Wms.Domain.Enums;
using Wms.Infrastructure.Persistence;

namespace Wms.Infrastructure.Operational;

/// <summary>
/// Метрики: товары, остатки и поставки — из БД; заказы нулевые до появления их модуля.
/// </summary>
internal sealed class OperationalMetrics(WmsDbContext db) : IOperationalMetrics, IProductSalesSource
{
    public async Task<PlatformOrderStats> GetPlatformStatsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-30);
        var inbound30d = await db.InboundShipments.CountAsync(s => s.CreatedAt >= since, ct);
        return new PlatformOrderStats(0, 0, 0, 0, inbound30d, new Dictionary<DateOnly, ChannelCounts>(), new Dictionary<string, int>());
    }

    public Task<int> CountOrders30dAsync(DateTimeOffset now, string? sellerId, IReadOnlyCollection<string>? warehouseIds, CancellationToken ct) => Task.FromResult(0);
    public Task<int> CountProductsAsync(string sellerId, CancellationToken ct) =>
        db.Products.CountAsync(p => p.SellerId == sellerId, ct);

    public Task<int> StockUnitsAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct) =>
        db.Stock.Where(s => s.SellerId == sellerId && warehouseIds.Contains(s.WarehouseId)).SumAsync(s => s.Quantity, ct);

    public Task<int> Sold30dAsync(string productId, IReadOnlyCollection<string>? warehouseIds, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<int>> OrdersTrendAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, IReadOnlyList<DateTimeOffset> dayStarts, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<int>>(dayStarts.Select(_ => 0).ToList());

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
