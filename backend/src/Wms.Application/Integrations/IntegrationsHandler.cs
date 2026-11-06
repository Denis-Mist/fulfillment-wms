using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Integrations;

public sealed record IntegrationLogDto(string Id, DateTimeOffset At, IntegrationLogKind Kind, string Message, int? Count);

public sealed record IntegrationDto(IntegrationChannel Channel, IntegrationStatus Status, string? KeyMask, string? ClientId, DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastSyncAt, bool AutoImportFbs, bool PushStock, int SyncIntervalMin, IReadOnlyList<IntegrationLogDto> Log,
    MarketplaceEnvironment Environment, string? LastError, int? LastHttpStatus);

public sealed record ConnectInput(string? ApiKey, string? ClientId, MarketplaceEnvironment? Environment = null);

public sealed record IntegrationSettingsInput(bool? AutoImportFbs, bool? PushStock, int? SyncIntervalMin, MarketplaceEnvironment? Environment = null);

/// <summary>Подключения кабинетов WB/Ozon. WB ходит в HTTP API; Ozon пока имитируется (следующий PR).</summary>
public sealed class IntegrationsHandler(IAppDbContext db, IUnitOfWork uow, IClock clock,
    IMarketplaceClientResolver clients, ISecretProtector secrets, IMarketplaceSettings settings)
{
    private static readonly IntegrationChannel[] Channels = [IntegrationChannel.Wb, IntegrationChannel.Ozon];

    public async Task<IReadOnlyList<IntegrationDto>> ListAsync(RequestContext ctx, CancellationToken ct)
    {
        if (ctx.OrganizationId is not { } orgId) return [];
        return (await EnsureAsync(orgId, ct)).Select(ToDto).ToList();
    }

    public async Task<IntegrationDto> ConnectAsync(RequestContext ctx, string channel, ConnectInput input, CancellationToken ct)
    {
        var row = await GetRowAsync(ctx, channel, ct);
        var environment = input.Environment ?? settings.DefaultEnvironment;
        try { row.Connect(input.ApiKey, input.ClientId, clock.UtcNow, environment); }
        catch (DomainValidationException e) { throw AppException.Unprocessable(e.Message, e.Detail); }
        row.SetApiKeyCipher(secrets.Protect(input.ApiKey!.Trim()));
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public Task<IntegrationDto> DisconnectAsync(RequestContext ctx, string channel, CancellationToken ct) =>
        MutateAsync(ctx, channel, i => i.Disconnect(), ct);

    public async Task<IntegrationDto> SyncAsync(RequestContext ctx, string channel, CancellationToken ct)
    {
        var row = await GetRowAsync(ctx, channel, ct);
        if (row.Status == IntegrationStatus.Disconnected)
            throw AppException.Conflict("Кабинет не подключён");

        // Ozon: имитация до PR с OzonSellerClient.
        if (row.Channel == IntegrationChannel.Ozon)
        {
            try { row.Sync(Random.Shared.Next(0, 7), 60 + Random.Shared.Next(0, 100), clock.UtcNow); }
            catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
            await uow.SaveChangesAsync(ct);
            return ToDto(row);
        }

        return await SyncWithClientAsync(row, ct);
    }

    public Task<IntegrationDto> UpdateAsync(RequestContext ctx, string channel, IntegrationSettingsInput input, CancellationToken ct) =>
        MutateAsync(ctx, channel, i => i.UpdateSettings(input.AutoImportFbs ?? false, input.PushStock ?? false, input.SyncIntervalMin ?? 0, input.Environment), ct);

    private async Task<IntegrationDto> SyncWithClientAsync(MarketplaceIntegration row, CancellationToken ct)
    {
        var key = secrets.TryUnprotect(row.ApiKeyCipher);
        if (string.IsNullOrWhiteSpace(key))
        {
            const string title = "Ключ API не сохранён";
            const string detail = "Подключите кабинет заново — ключ хранится только после повторного подключения.";
            row.RecordSyncFailure($"{title}. {detail}", null, clock.UtcNow);
            await uow.SaveChangesAsync(ct);
            throw AppException.Unprocessable(title, detail);
        }

        var creds = new MarketplaceCredentials(key, row.ClientId, row.Environment);
        var client = clients.For(row.Channel);
        try
        {
            await client.PingAsync(creds, ct);
            var orders = await client.GetFbsOrderCountAsync(creds, ct);
            _ = await client.GetCardCountAsync(creds, ct);
            int? pushed = null;
            if (row.PushStock && CanPushStocks(row))
            {
                var stocks = await LoadStocksAsync(row.OrganizationId, row.Channel, ct);
                pushed = await client.PushStocksAsync(creds, stocks, ct);
            }
            row.RecordSyncSuccess(orders, pushed, clock.UtcNow, 200);
            await uow.SaveChangesAsync(ct);
            return ToDto(row);
        }
        catch (MarketplaceApiException ex)
        {
            row.RecordSyncFailure(ex.Message, ex.HttpStatus, clock.UtcNow);
            await uow.SaveChangesAsync(ct);
            throw ex.IsClientError
                ? AppException.Unprocessable("Ошибка API маркетплейса", ex.Message)
                : AppException.BadGateway("Ошибка API маркетплейса", ex.Message);
        }
    }

    private bool CanPushStocks(MarketplaceIntegration row) =>
        row.Environment == MarketplaceEnvironment.Sandbox || settings.AllowProductionStockPush;

    private async Task<IReadOnlyList<MarketplaceStockItem>> LoadStocksAsync(string orgId, IntegrationChannel channel, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.SellerId == orgId && !p.Archived).ToListAsync(ct);
        if (products.Count == 0) return [];
        var qty = await db.Stock.AsNoTracking().Where(s => s.SellerId == orgId)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Amount = g.Sum(x => x.Quantity - x.Reserved) })
            .ToListAsync(ct);
        var byProduct = qty.ToDictionary(x => x.ProductId, x => Math.Max(0, x.Amount));
        var items = new List<MarketplaceStockItem>();
        foreach (var product in products)
        {
            var sku = channel == IntegrationChannel.Wb
                ? (string.IsNullOrWhiteSpace(product.WbBarcode) ? product.Barcode : product.WbBarcode)
                : (string.IsNullOrWhiteSpace(product.Sku) ? product.Barcode : product.Sku);
            if (string.IsNullOrWhiteSpace(sku)) continue;
            items.Add(new MarketplaceStockItem(sku, byProduct.GetValueOrDefault(product.Id)));
        }
        return items;
    }

    private async Task<IntegrationDto> MutateAsync(RequestContext ctx, string channel, Action<MarketplaceIntegration> action, CancellationToken ct)
    {
        var row = await GetRowAsync(ctx, channel, ct);
        try { action(row); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        catch (DomainValidationException e) { throw AppException.Unprocessable(e.Message, e.Detail); }
        await uow.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private async Task<MarketplaceIntegration> GetRowAsync(RequestContext ctx, string channel, CancellationToken ct)
    {
        if (!SnakeCaseName.TryParse<IntegrationChannel>(channel, out var ch) || ctx.OrganizationId is not { } orgId)
            throw AppException.NotFound("Неизвестный маркетплейс");
        await EnsureAsync(orgId, ct);
        return await db.Integrations.FirstAsync(i => i.OrganizationId == orgId && i.Channel == ch, ct);
    }

    /// <summary>Демо-состояние создаётся при первом обращении организации (storeFor() из мока).</summary>
    private async Task<List<MarketplaceIntegration>> EnsureAsync(string orgId, CancellationToken ct)
    {
        var rows = await db.Integrations.Where(i => i.OrganizationId == orgId).ToListAsync(ct);
        if (rows.Count == Channels.Length) return rows.OrderBy(r => r.Channel).ToList();
        var seed = SeededRandom.Hash(orgId, 11);
        var now = clock.UtcNow;
        for (var i = 0; i < Channels.Length; i++)
        {
            var ch = Channels[i];
            if (rows.Any(r => r.Channel == ch)) continue;
            var row = new MarketplaceIntegration(orgId, ch);
            if (ch == IntegrationChannel.Wb || seed % 2 == 0)
            {
                var mask = ToBase36(seed + (uint)(i * 977));
                row.Restore($"••••{mask[^Math.Min(4, mask.Length)..]}", ch == IntegrationChannel.Ozon ? (100000 + seed % 899999).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    now.AddDays(-(40 + i * 11)), now.AddMinutes(-(8 + i * 5)), ch == IntegrationChannel.Wb, SeedLog(ch, seed + (uint)i, now));
            }
            db.Integrations.Add(row);
            rows.Add(row);
        }
        await uow.SaveChangesAsync(ct);
        return rows.OrderBy(r => r.Channel).ToList();
    }

    private static IEnumerable<IntegrationLogEntry> SeedLog(IntegrationChannel ch, uint seed, DateTimeOffset now)
    {
        var rng = new SeededRandom(seed);
        var prefix = ch == IntegrationChannel.Wb ? "wb" : "ozon";
        for (var i = 0; i < 14; i++)
        {
            var at = now.AddHours(-(i * 2 + rng.Float(0, 1)));
            if (i == 5 && ch == IntegrationChannel.Ozon)
            {
                yield return new($"{prefix}_{i}", at, IntegrationLogKind.Error, "Таймаут ответа API маркетплейса, повтор через 5 мин");
                continue;
            }
            var stock = i % 3 == 1;
            var count = stock ? rng.Int(40, 180) : rng.Int(0, 12);
            yield return new($"{prefix}_{i}", at, stock ? IntegrationLogKind.Stock : IntegrationLogKind.Orders,
                stock ? $"Остатки выгружены: {count} SKU" : $"Загружено заказов FBS: {count}", count);
        }
    }

    private static string ToBase36(uint value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var s = string.Empty;
        do { s = digits[(int)(value % 36)] + s; value /= 36; } while (value > 0);
        return s;
    }

    private static IntegrationDto ToDto(MarketplaceIntegration i) => new(i.Channel, i.Status, i.KeyMask, i.ClientId, i.ConnectedAt, i.LastSyncAt,
        i.AutoImportFbs, i.PushStock, i.SyncIntervalMin, i.Log.Select(l => new IntegrationLogDto(l.Id, l.At, l.Kind, l.Message, l.Count)).ToList(),
        i.Environment, i.LastError, i.LastHttpStatus);
}
