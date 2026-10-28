using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Catalog;
using Wms.Infrastructure.Persistence;

namespace Wms.Infrastructure.Operational;

/// <summary>
/// Метрики: товары и остатки — из каталога; заказы и поставки нулевые до появления их модулей (PR4+).
/// </summary>
internal sealed class OperationalMetrics(WmsDbContext db) : IOperationalMetrics, IProductSalesSource
{
    public Task<PlatformOrderStats> GetPlatformStatsAsync(DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(new PlatformOrderStats(0, 0, 0, 0, 0, new Dictionary<DateOnly, ChannelCounts>(), new Dictionary<string, int>()));

    public Task<int> CountOrders30dAsync(DateTimeOffset now, string? sellerId, IReadOnlyCollection<string>? warehouseIds, CancellationToken ct) => Task.FromResult(0);
    public Task<int> CountProductsAsync(string sellerId, CancellationToken ct) =>
        db.Products.CountAsync(p => p.SellerId == sellerId, ct);

    public Task<int> StockUnitsAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct) =>
        db.Stock.Where(s => s.SellerId == sellerId && warehouseIds.Contains(s.WarehouseId)).SumAsync(s => s.Quantity, ct);

    public Task<int> Sold30dAsync(string productId, IReadOnlyCollection<string>? warehouseIds, DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<int>> OrdersTrendAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, IReadOnlyList<DateTimeOffset> dayStarts, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<int>>(dayStarts.Select(_ => 0).ToList());

    public Task<int> ActiveInboundAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct) => Task.FromResult(0);
}

/// <summary>Временный пароль формата моков: wms-xxxxxx.</summary>
internal sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    public string Generate() => "wms-" + RandomNumberGenerator.GetString(Alphabet, 6);
}
