using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Billing;

public sealed record BillingContractDto(string ContractId, string ContractNumber, string CounterpartyName);

public sealed record ChargeItemDto(string Id, string ContractId, string CounterpartyName, string Date, BillingService Service, decimal Quantity,
    string Unit, decimal Rate, decimal Amount);

public sealed record ServiceAmountDto(BillingService Service, decimal Amount);

public sealed record DailyChargesDto(string Date, IReadOnlyDictionary<string, decimal> Amounts);

public sealed record BillingSummaryDto(decimal MonthAccrued, decimal PrevMonthAccrued, decimal Unpaid, decimal Balance,
    IReadOnlyList<ServiceAmountDto> ByService, IReadOnlyList<DailyChargesDto> Daily);

public sealed record ActItemDto(string Id, string Number, string ContractId, string CounterpartyName, string Period, decimal Amount, ActStatus Status,
    DateTimeOffset? IssuedAt, DateTimeOffset? PaidAt, DateTimeOffset? DueAt);

public sealed record TariffDto(decimal ReceivingPerUnit, decimal StoragePerM3Day, decimal PickPerOrder, decimal PackPerUnit, decimal FboPerBox, decimal ReturnPerUnit);

public sealed record TariffInput(decimal? ReceivingPerUnit, decimal? StoragePerM3Day, decimal? PickPerOrder, decimal? PackPerUnit, decimal? FboPerBox, decimal? ReturnPerUnit);

public sealed record ChargeQuery(PageQuery Page, string? ContractId, BillingService? Service);

/// <summary>Биллинг фулфилмента (mocks/handlers/billing.ts): договоры, тарифы, начисления и акты.</summary>
public sealed class BillingHandler(IAppDbContext db, IUnitOfWork uow, IClock clock, IBillingStore store)
{
    public async Task<IReadOnlyList<BillingContractDto>> ContractsAsync(RequestContext ctx, CancellationToken ct)
    {
        var (contracts, names) = await VisibleAsync(ctx, ct);
        return contracts.Select(c => new BillingContractDto(c.Id, c.Number, Counterparty(ctx, c, names))).ToList();
    }

    public async Task<BillingSummaryDto> SummaryAsync(RequestContext ctx, string? contractId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var list = await ChargesAsync(ctx, contractId, ct);
        var cur = ChargeCalculator.MonthKey(now);
        var prevDate = new DateTimeOffset(now.Year, now.Month, 15, 0, 0, 0, TimeSpan.Zero).AddMonths(-1);
        var prev = ChargeCalculator.MonthKey(prevDate);
        decimal Sum(Func<ChargeItemDto, bool> f) => Round(list.Where(f).Sum(x => x.Amount));
        var byService = Enum.GetValues<BillingService>().Select(s => new ServiceAmountDto(s, Sum(x => x.Service == s && x.Date.StartsWith(cur, StringComparison.Ordinal)))).ToList();
        var since = ChargeCalculator.DayKey(now.AddDays(-29));
        var daily = list.Where(x => string.CompareOrdinal(x.Date, since) >= 0)
            .GroupBy(x => x.Date).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new DailyChargesDto(g.Key, g.GroupBy(x => x.Service).ToDictionary(s => SnakeCaseName.Of(s.Key), s => Round(s.Sum(x => x.Amount)))))
            .ToList();
        var unpaid = (await ActsAsync(ctx, contractId, ct)).Where(a => a.Status == ActStatus.Issued).Sum(a => a.Amount);
        var month = Sum(x => x.Date.StartsWith(cur, StringComparison.Ordinal));
        return new BillingSummaryDto(month, Sum(x => x.Date.StartsWith(prev, StringComparison.Ordinal)), Round(unpaid), -Round(unpaid + month), byService, daily);
    }

    public async Task<Paged<ChargeItemDto>> ChargesPageAsync(RequestContext ctx, ChargeQuery query, CancellationToken ct)
    {
        var items = (await ChargesAsync(ctx, query.ContractId, ct)).Where(x => query.Service is null || x.Service == query.Service);
        return Paged<ChargeItemDto>.From(items.SortBy(SortKey(query.Page.SortField ?? "date"), query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }

    public async Task<IReadOnlyList<ActItemDto>> ActsAsync(RequestContext ctx, string? contractId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var current = ChargeCalculator.MonthKey(now);
        var (contracts, names) = await VisibleAsync(ctx, ct);
        contracts = contracts.Where(c => string.IsNullOrEmpty(contractId) || c.Id == contractId).ToList();
        var ids = contracts.Select(c => c.Id).ToList();
        var acts = await db.BillingActs.AsNoTracking().Where(a => ids.Contains(a.ContractId)).ToListAsync(ct);
        var list = new List<ActItemDto>();
        foreach (var c in contracts)
        {
            var own = acts.Where(a => a.ContractId == c.Id).ToList();
            list.AddRange(own.Select(a => ToDto(a, Counterparty(ctx, c, names))));
            if (own.Any(a => a.Period == current)) continue;
            var amount = (await BuildAsync([c], now, ct)).Where(x => x.Date.StartsWith(current, StringComparison.Ordinal)).Sum(x => x.Amount);
            list.Add(new ActItemDto($"draft_{c.Id}", BillingAct.NumberFor(c.Id, current), c.Id, Counterparty(ctx, c, names), current,
                Math.Round(amount, 2, MidpointRounding.AwayFromZero), ActStatus.Draft, null, null, null));
        }
        var ru = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;
        return list.OrderByDescending(a => a.Period, StringComparer.Ordinal)
            .ThenBy(a => a.CounterpartyName, Comparer<string>.Create((x, y) => ru.Compare(x, y))).ToList();
    }

    public async Task<ActItemDto> IssueAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var draft = (await ActsAsync(ctx, null, ct)).FirstOrDefault(a => a.Id == id && a.Status == ActStatus.Draft)
                    ?? throw AppException.NotFound("Черновик акта не найден");
        var act = new BillingAct($"act_{draft.ContractId}_{draft.Period}", draft.ContractId, draft.Period, draft.Amount, ActStatus.Issued, clock.UtcNow, null);
        if (!await store.TryAddActAsync(act, ct)) throw AppException.Conflict("Акт уже выставлен", "Обновите список актов.");
        return ToDto(act, draft.CounterpartyName);
    }

    public async Task<ActItemDto> PayAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var act = await db.BillingActs.FirstOrDefaultAsync(a => a.Id == id, ct);
        var (contracts, names) = await VisibleAsync(ctx, ct);
        var contract = act is null ? null : contracts.FirstOrDefault(c => c.Id == act.ContractId);
        if (act is null || contract is null) throw AppException.NotFound("Акт не найден");
        try { act.Pay(clock.UtcNow); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        await uow.SaveChangesAsync(ct);
        return ToDto(act, Counterparty(ctx, contract, names));
    }

    public async Task<TariffDto> TariffAsync(RequestContext ctx, string contractId, CancellationToken ct)
    {
        await OwnContractAsync(ctx, contractId, ct);
        return ToDto(await db.Tariffs.AsNoTracking().FirstOrDefaultAsync(t => t.ContractId == contractId, ct) ?? Tariff.Default(contractId));
    }

    public async Task<TariffDto> UpdateTariffAsync(RequestContext ctx, string contractId, TariffInput input, CancellationToken ct)
    {
        await OwnContractAsync(ctx, contractId, ct);
        await store.AddMissingAsync([Tariff.Default(contractId)], [], ct);
        var tariff = await db.Tariffs.FirstAsync(t => t.ContractId == contractId, ct);
        static decimal Req(decimal? v, string field) => v ?? throw AppException.Unprocessable("Некорректный тариф", field);
        try
        {
            tariff.Update(Req(input.ReceivingPerUnit, "receivingPerUnit"), Req(input.StoragePerM3Day, "storagePerM3Day"), Req(input.PickPerOrder, "pickPerOrder"),
                Req(input.PackPerUnit, "packPerUnit"), Req(input.FboPerBox, "fboPerBox"), Req(input.ReturnPerUnit, "returnPerUnit"));
        }
        catch (DomainValidationException e) { throw AppException.Unprocessable(e.Message, e.Detail); }
        await uow.SaveChangesAsync(ct);
        return ToDto(tariff);
    }

    private async Task OwnContractAsync(RequestContext ctx, string contractId, CancellationToken ct)
    {
        if ((await VisibleAsync(ctx, ct)).Contracts.All(c => c.Id != contractId)) throw AppException.NotFound("Договор не найден");
    }

    private async Task<List<ChargeItemDto>> ChargesAsync(RequestContext ctx, string? contractId, CancellationToken ct)
    {
        var (contracts, names) = await VisibleAsync(ctx, ct);
        var chosen = contracts.Where(c => string.IsNullOrEmpty(contractId) || c.Id == contractId).ToList();
        var byId = chosen.ToDictionary(c => c.Id);
        return (await BuildAsync(chosen, clock.UtcNow, ct))
            .Select(x => new ChargeItemDto(x.Id, x.ContractId, Counterparty(ctx, byId[x.ContractId], names), x.Date, x.Service, x.Quantity, x.Unit, x.Rate, x.Amount))
            .ToList();
    }

    private async Task<List<Charge>> BuildAsync(IReadOnlyList<Contract> contracts, DateTimeOffset now, CancellationToken ct)
    {
        if (contracts.Count == 0) return [];
        var ids = contracts.Select(c => c.Id).ToList();
        var sellers = contracts.Select(c => c.SellerId).Distinct().ToList();
        var since = now.AddDays(-ChargeCalculator.HistoryDays);
        var tariffs = await db.Tariffs.AsNoTracking().Where(t => ids.Contains(t.ContractId)).ToDictionaryAsync(t => t.ContractId, ct);
        var orders = await db.Orders.AsNoTracking().Where(o => sellers.Contains(o.SellerId) && o.CreatedAt >= since).ToListAsync(ct);
        var inbound = await db.InboundShipments.AsNoTracking().Where(i => sellers.Contains(i.SellerId) && i.ExpectedAt >= since).ToListAsync(ct);
        var volumes = await db.Products.AsNoTracking().Where(p => sellers.Contains(p.SellerId)).ToDictionaryAsync(p => p.Id, p => p.VolumeLiters, ct);
        var stock = await db.Stock.AsNoTracking().Where(s => sellers.Contains(s.SellerId)).ToListAsync(ct);
        var result = new List<Charge>();
        foreach (var c in contracts)
        {
            var wh = c.WarehouseIds.ToHashSet();
            var liters = stock.Where(s => s.SellerId == c.SellerId && wh.Contains(s.WarehouseId))
                .Sum(s => s.Quantity * (volumes.TryGetValue(s.ProductId, out var v) ? v : 1));
            result.AddRange(ChargeCalculator.Build(c, tariffs.GetValueOrDefault(c.Id) ?? Tariff.Default(c.Id),
                new ContractActivity(orders, inbound, liters), now));
        }
        return result;
    }

    /// <summary>Видимые договоры (только чтение — без побочных эффектов).</summary>
    private async Task<(List<Contract> Contracts, Dictionary<string, string> Names)> VisibleAsync(RequestContext ctx, CancellationToken ct)
    {
        var query = db.Contracts.AsNoTracking();
        if (ctx.IsFulfillment) query = query.Where(c => c.FulfillmentId == ctx.OrganizationId);
        else if (!ctx.IsPlatformAdmin) query = query.Where(c => c.SellerId == ctx.OrganizationId);
        var contracts = (await query.ToListAsync(ct)).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return (contracts, names);
    }

    /// <summary>
    /// Заводит договорам без тарифа тариф по умолчанию и акты за три прошлых месяца.
    /// Вызывается при старте (после сида), а не из чтений; запись идемпотентна (ON CONFLICT DO NOTHING).
    /// </summary>
    public async Task ProvisionAsync(CancellationToken ct)
    {
        var contracts = (await db.Contracts.AsNoTracking().ToListAsync(ct)).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var ids = contracts.Select(c => c.Id).ToList();
        var known = (await db.Tariffs.AsNoTracking().Where(t => ids.Contains(t.ContractId)).Select(t => t.ContractId).ToListAsync(ct)).ToHashSet();
        var missing = contracts.Where(c => !known.Contains(c.Id)).ToList();
        if (missing.Count == 0) return;
        var now = clock.UtcNow;
        var charges = await BuildAsync(missing, now, ct);
        var tariffs = new List<Tariff>();
        var acts = new List<BillingAct>();
        foreach (var c in missing)
        {
            tariffs.Add(Tariff.Default(c.Id));
            for (var m = 3; m >= 1; m--)
            {
                var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-m);
                var period = ChargeCalculator.MonthKey(monthStart);
                var amount = Math.Round(charges.Where(x => x.ContractId == c.Id && x.Date.StartsWith(period, StringComparison.Ordinal)).Sum(x => x.Amount), 2,
                    MidpointRounding.AwayFromZero);
                if (amount == 0) continue;
                var issued = monthStart.AddMonths(1).AddDays(2);
                acts.Add(new BillingAct($"act_{c.Id}_{period}", c.Id, period, amount, m > 1 ? ActStatus.Paid : ActStatus.Issued, issued,
                    m > 1 ? issued.AddDays(6) : null));
            }
        }
        await store.AddMissingAsync(tariffs, acts, ct);
    }

    private static string Counterparty(RequestContext ctx, Contract c, Dictionary<string, string> names) =>
        names.GetValueOrDefault(ctx.IsSeller ? c.FulfillmentId : c.SellerId) ?? "—";

    private static decimal Round(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

    private static ActItemDto ToDto(BillingAct a, string counterparty) =>
        new(a.Id, BillingAct.NumberFor(a.ContractId, a.Period), a.ContractId, counterparty, a.Period, a.Amount, a.Status, a.IssuedAt, a.PaidAt, a.DueAt);

    private static TariffDto ToDto(Tariff t) => new(t.ReceivingPerUnit, t.StoragePerM3Day, t.PickPerOrder, t.PackPerUnit, t.FboPerBox, t.ReturnPerUnit);

    private static Func<ChargeItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => x => x.Id,
        "contractId" => x => x.ContractId,
        "counterpartyName" => x => x.CounterpartyName,
        "date" => x => x.Date,
        "service" => x => SnakeCaseName.Of(x.Service),
        "quantity" => x => x.Quantity,
        "unit" => x => x.Unit,
        "rate" => x => x.Rate,
        "amount" => x => x.Amount,
        _ => null,
    };
}
