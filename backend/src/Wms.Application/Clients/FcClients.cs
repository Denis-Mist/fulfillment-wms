using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Clients;

public sealed record NamedRef(string Id, string Name);

public sealed record ClientListItem(string Id, string Name, string Inn, string City, string ContractId, string ContractNumber,
    DateTimeOffset StartedAt, IReadOnlyList<NamedRef> Warehouses, int SkuCount, int StockUnits, int Orders30d,
    IReadOnlyList<string> Marketplaces);

public sealed record ClientTariff(double StoragePerLiterDay, int PickPerOrder, int ReceivingPerUnit);

public sealed record TrendPoint(DateTimeOffset Date, int Count);

public sealed record ClientDetails(string Id, string Name, string Inn, string City, string ContractId, string ContractNumber,
    DateTimeOffset StartedAt, IReadOnlyList<NamedRef> Warehouses, int SkuCount, int StockUnits, int Orders30d,
    IReadOnlyList<string> Marketplaces, string OwnerName, string OwnerEmail, string OwnerPhone, ClientTariff Tariff,
    IReadOnlyList<TrendPoint> OrdersTrend, int InboundActive);

/// <summary>Тариф договора — детерминированно из идентификатора договора (как tariffFor в моках).</summary>
public static class ContractTariff
{
    public static ClientTariff For(string contractId)
    {
        var seed = contractId.Sum(ch => (int)ch);
        return new ClientTariff(0.35 + seed % 5 * 0.05, 45 + seed % 4 * 5, 6 + seed % 3);
    }
}

/// <summary>Сборка карточки клиента ФЦ из договора, организации и операционных метрик.</summary>
public sealed class ClientItemBuilder(IAppDbContext db, IOperationalMetrics metrics, IClock clock)
{
    public async Task<ClientListItem> BuildAsync(Contract c, IReadOnlyDictionary<string, Organization> orgs,
        IReadOnlyDictionary<string, string> warehouseNames, CancellationToken ct)
    {
        orgs.TryGetValue(c.SellerId, out var org);
        return new ClientListItem(c.SellerId, org?.Name ?? "—", org?.Inn ?? "", org?.City ?? "", c.Id, c.Number, c.StartedAt,
            c.WarehouseIds.Select(id => new NamedRef(id, warehouseNames.GetValueOrDefault(id) ?? id)).ToList(),
            await metrics.CountProductsAsync(c.SellerId, ct),
            await metrics.StockUnitsAsync(c.SellerId, c.WarehouseIds, ct),
            await metrics.CountOrders30dAsync(clock.UtcNow, c.SellerId, c.WarehouseIds, ct),
            org?.Marketplaces?.Select(SnakeCaseName.Of).ToList() ?? []);
    }

    public async Task<(IReadOnlyDictionary<string, Organization> Orgs, IReadOnlyDictionary<string, string> Warehouses)> LookupsAsync(CancellationToken ct) =>
        (await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, ct),
         await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct));
}

public sealed class FcClientsQueryHandler(IAppDbContext db, ClientItemBuilder builder)
{
    public async Task<IReadOnlyList<ClientListItem>> HandleAsync(RequestContext ctx, string? search, CancellationToken ct)
    {
        var term = (search ?? string.Empty).ToLowerInvariant();
        var (orgs, warehouses) = await builder.LookupsAsync(ct);
        var contracts = db.Contracts.AsNoTracking();
        if (!ctx.IsPlatformAdmin) contracts = contracts.Where(c => c.FulfillmentId == ctx.OrganizationId);
        var items = new List<ClientListItem>();
        foreach (var c in await contracts.OrderBy(c => c.Id).ToListAsync(ct))
            items.Add(await builder.BuildAsync(c, orgs, warehouses, ct));
        return items.Where(c => term.Length == 0 || c.Name.ToLowerInvariant().Contains(term) || c.Inn.Contains(term))
            .OrderByDescending(c => c.Orders30d).ToList();
    }
}

public sealed class FcClientDetailsHandler(IAppDbContext db, ClientItemBuilder builder, IOperationalMetrics metrics, IClock clock)
{
    public async Task<ClientDetails> HandleAsync(RequestContext ctx, string sellerId, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking()
            .Where(c => c.SellerId == sellerId && (ctx.IsPlatformAdmin || c.FulfillmentId == ctx.OrganizationId))
            .OrderBy(c => c.Id).FirstOrDefaultAsync(ct) ?? throw AppException.NotFound("Клиент не найден");
        var (orgs, warehouses) = await builder.LookupsAsync(ct);
        var b = await builder.BuildAsync(contract, orgs, warehouses, ct);
        var owner = (await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct))
            .FirstOrDefault(u => u.Memberships.Any(m => m.OrganizationId == sellerId && m.Role == Role.SellerOwner));
        // Начало текущих суток (UTC) и 13 предыдущих
        var today = new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var days = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).ToList();
        var counts = await metrics.OrdersTrendAsync(sellerId, contract.WarehouseIds, days, ct);
        return new ClientDetails(b.Id, b.Name, b.Inn, b.City, b.ContractId, b.ContractNumber, b.StartedAt, b.Warehouses,
            b.SkuCount, b.StockUnits, b.Orders30d, b.Marketplaces, owner?.FullName ?? "—", owner?.Email ?? "—", owner?.Phone ?? "—",
            ContractTariff.For(contract.Id), days.Select((d, i) => new TrendPoint(d, counts[i])).ToList(),
            await metrics.ActiveInboundAsync(sellerId, contract.WarehouseIds, ct));
    }
}
