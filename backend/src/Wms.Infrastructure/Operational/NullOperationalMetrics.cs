using System.Security.Cryptography;
using Wms.Application.Abstractions;

namespace Wms.Infrastructure.Operational;

/// <summary>
/// Заглушка до появления модулей заказов/поставок/остатков (PR4+): все метрики нулевые.
/// </summary>
internal sealed class NullOperationalMetrics : IOperationalMetrics
{
    public Task<PlatformOrderStats> GetPlatformStatsAsync(DateTimeOffset now, CancellationToken ct) =>
        Task.FromResult(new PlatformOrderStats(0, 0, 0, 0, 0, new Dictionary<DateOnly, ChannelCounts>(), new Dictionary<string, int>()));

    public Task<int> CountOrders30dAsync(DateTimeOffset now, string? sellerId, IReadOnlyCollection<string>? warehouseIds, CancellationToken ct) => Task.FromResult(0);
    public Task<int> CountProductsAsync(string sellerId, CancellationToken ct) => Task.FromResult(0);
    public Task<int> StockUnitsAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct) => Task.FromResult(0);

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
