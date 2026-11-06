using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Analytics;

public sealed record CalcMeta(string Algorithm, string Version, uint Seed, string DataVersion, DateTimeOffset ComputedAt, int DurationMs);
public sealed record ForecastPoint(DateTimeOffset Date, int? Actual, int? Predicted, int? Low = null, int? High = null);
public sealed record BacktestDto(double Mape, int Mae, double BaselineMape, int Days);
public sealed record StaffingDay(DateTimeOffset Date, int Orders, int Units, int Pickers, int Packers, int Receivers, int Total, int Available);
public sealed record PlanDto(DateTimeOffset ApprovedAt, string ApprovedBy, ForecastAlgorithm Algorithm);
public sealed record ForecastResult(CalcMeta Meta, int Horizon, IReadOnlyList<ForecastPoint> Series, BacktestDto Backtest,
    IReadOnlyList<StaffingDay> Staffing, PlanDto? Plan);
public sealed record ApproveInput(string? WarehouseId, string? Algorithm);

public sealed record SlottingMove(string Id, string ProductId, string Sku, string Name, string SellerName, string Abc, int Picks30d,
    string? AffinitySku, string FromCell, double FromDistance, string ToCell, double ToDistance, int Gain, bool Applied,
    double VolumeLiters = 0, double WeightG = 0, double CapacityLiters = 0);
public sealed record ClassShare(int Sku, int PicksShare);
public sealed record SlottingResult(CalcMeta Meta, IReadOnlyDictionary<string, ClassShare> Classes, int CurrentDistance, int ProjectedDistance,
    double ReductionPct, IReadOnlyList<SlottingMove> Moves);
public sealed record ApplyInput(IReadOnlyList<string>? Ids);
public sealed record ApplyResult(int Applied);

public sealed record KpiPoint(DateTimeOffset Date, int Orders, double ProcessingHours, int RouteBaseline, int RouteOptimized, int Utilization, int Errors);
public sealed record KpiTotals(int Orders, int ThroughputPerDay, double AvgProcessingHours, int AvgRouteBaseline, int AvgRouteOptimized,
    double RouteReductionPct, int Utilization, double ErrorRate);
public sealed record SellerKpi(string SellerId, string Name, int Orders, double AvgProcessingHours, double ErrorRate);
public sealed record KpiReport(int Period, KpiTotals Totals, IReadOnlyList<KpiPoint> Series, IReadOnlyList<SellerKpi> BySeller);

/// <summary>Аналитика ФЦ (mocks/handlers/analytics.ts): прогноз и план персонала, слоттинг ABC, KPI маршрутизации.</summary>
public sealed class AnalyticsHandler(IAppDbContext db, IUnitOfWork uow, IClock clock, WarehouseScope scope)
{
    /// <summary>Нормативы производительности, единиц в смену на сотрудника.</summary>
    private const int PickerRate = 420, PackerRate = 260, ReceiverRate = 900;
    private static readonly Role[] FloorRoles = [Role.FcPicker, Role.FcPacker, Role.FcReceiver];

    private DateTimeOffset Today => new(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

    private async Task<List<Warehouse>> WarehousesAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var visible = await scope.VisibleWarehouseIdsAsync(ctx, ct);
        return (await db.Warehouses.AsNoTracking().OrderBy(w => EF.Property<long>(w, "Seq")).ToListAsync(ct))
            .Where(w => (visible is null || visible.Contains(w.Id)) && (string.IsNullOrEmpty(warehouseId) || w.Id == warehouseId)).ToList();
    }

    private CalcMeta Meta(string algorithm, uint seed, Stopwatch sw, string? dataVersion = null, string? version = null) => new(algorithm,
        version ?? (algorithm is "holt_winters" or "abc_affinity" ? "1.3.0" : "1.0.0"), seed,
        dataVersion ?? $"snap-{Today:yyyy-MM-dd}", clock.UtcNow, Math.Max(4, (int)sw.ElapsedMilliseconds));

    public async Task<ForecastResult> ForecastAsync(RequestContext ctx, string? warehouseId, int? horizonParam, ForecastAlgorithm? algorithmParam, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var horizon = Math.Clamp(horizonParam ?? 14, 7, 28);
        var algorithm = algorithmParam ?? ForecastAlgorithm.HoltWinters;
        var warehouses = await WarehousesAsync(ctx, warehouseId, ct);
        if (warehouses.Count == 0) throw AppException.NotFound("Склад не найден");
        var seed = SeededRandom.Hash(string.Join(',', warehouses.Select(w => w.Id)));
        var orgIds = warehouses.Select(w => w.OrganizationId).Distinct().ToList();
        var available = await db.Users.AsNoTracking()
            .CountAsync(u => u.Memberships.Any(m => orgIds.Contains(m.OrganizationId) && FloorRoles.Contains(m.Role)), ct);
        var today = Today;
        var from = today.AddDays(-Forecasting.HistoryDays);
        var whIds = warehouses.Select(w => w.Id).ToList();
        var orders = await db.Orders.AsNoTracking()
            .Where(o => whIds.Contains(o.WarehouseId) && o.CreatedAt >= from && o.CreatedAt < today && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        var pickTasks = await db.Tasks.AsNoTracking()
            .Where(t => whIds.Contains(t.WarehouseId) && t.Type == TaskType.Picking && t.Status != WarehouseTaskStatus.Cancelled
                && t.CreatedAt >= from && t.CreatedAt < today)
            .ToListAsync(ct);
        var events = orders.Select(o => new DemandEvent(o.CreatedAt, 1, o.Units, o.Lines.Count))
            .Concat(pickTasks.Select(t => new DemandEvent(t.FinishedAt ?? t.CreatedAt, 0, 0, t.Items.Count)));
        var daily = DemandHistory.BuildUtc(today, Forecasting.HistoryDays, events);
        var hist = Forecasting.FromDemand(daily);
        var unitsPerOrder = DemandHistory.AverageUnitsPerOrder(daily);
        var values = hist.Select(h => (double)h.Value).ToList();
        var train = values.Take(values.Count - Forecasting.BacktestDays).ToList();
        var test = values.Skip(values.Count - Forecasting.BacktestDays).ToList();
        var back = Forecasting.Predict(algorithm, train, Forecasting.BacktestDays);
        var err = Forecasting.Errors(test, back);
        var baseErr = Forecasting.Errors(test, Forecasting.MovingAverage(train, Forecasting.BacktestDays));
        var future = Forecasting.Predict(algorithm, values, horizon);
        var spread = 1.28 * err.Mae;
        var start = Forecasting.HistoryDays - Forecasting.BacktestDays;

        var series = hist.Select((h, i) => new ForecastPoint(h.Date, h.Value, i >= start ? Forecasting.Round(back[i - start]) : null)).ToList();
        var staffing = new List<StaffingDay>();
        for (var h = 0; h < future.Length; h++)
        {
            var v = future[h];
            var date = today.AddDays(h);
            var predictedOrders = Math.Max(0, Forecasting.Round(v));
            series.Add(new ForecastPoint(date, null, predictedOrders, Math.Max(0, Forecasting.Round(v - spread)), Forecasting.Round(v + spread)));
            var units = Forecasting.Round(predictedOrders * unitsPerOrder);
            int pickers = (int)Math.Ceiling(units / (double)PickerRate), packers = (int)Math.Ceiling(units / (double)PackerRate),
                receivers = (int)Math.Ceiling(units * 0.9 / ReceiverRate);
            staffing.Add(new StaffingDay(date, predictedOrders, units, pickers, packers, receivers, pickers + packers + receivers, available));
        }
        var key = StaffPlan.KeyFor(ctx.OrganizationId, string.IsNullOrEmpty(warehouseId) ? null : warehouseId);
        var plan = await db.StaffPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == key, ct);
        return new ForecastResult(Meta(SnakeCaseName.Of(algorithm), seed, sw,
            $"orders-{orders.Count}-picks-{pickTasks.Sum(t => t.Items.Count)}-snap-{today:yyyy-MM-dd}", "2.0.0"), horizon, series,
            new BacktestDto(err.Mape, err.Mae, baseErr.Mape, Forecasting.BacktestDays), staffing,
            plan is null ? null : new PlanDto(plan.ApprovedAt, plan.ApprovedBy, plan.Algorithm));
    }

    public async Task<PlanDto> ApproveAsync(RequestContext ctx, ApproveInput input, CancellationToken ct)
    {
        // Как мок: без algorithm план утверждается (алгоритм по умолчанию); неизвестное значение — 422.
        var algorithm = ForecastAlgorithm.HoltWinters;
        if (!string.IsNullOrEmpty(input.Algorithm) && !SnakeCaseName.TryParse(input.Algorithm, out algorithm))
            throw AppException.Unprocessable("Неизвестный алгоритм", "algorithm");
        var key = StaffPlan.KeyFor(ctx.OrganizationId, string.IsNullOrEmpty(input.WarehouseId) ? null : input.WarehouseId);
        var by = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? string.Empty;
        var plan = await db.StaffPlans.FirstOrDefaultAsync(p => p.Id == key, ct);
        if (plan is null) db.StaffPlans.Add(plan = new StaffPlan(key, algorithm, by, clock.UtcNow));
        else plan.Approve(algorithm, by, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return new PlanDto(plan.ApprovedAt, plan.ApprovedBy, plan.Algorithm);
    }

    private static double Distance(StorageLocation l) => SlottingPolicy.Distance(l.X, l.Y, l.Level);

    public async Task<SlottingResult> SlottingAsync(RequestContext ctx, string? warehouseId, SlottingAlgorithm? algorithmParam, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var algorithm = algorithmParam ?? SlottingAlgorithm.AbcAffinity;
        var algoName = SnakeCaseName.Of(algorithm);
        var whIds = (await WarehousesAsync(ctx, warehouseId, ct)).Select(w => w.Id).ToList();
        var seed = SeededRandom.Hash(string.Join(',', whIds));
        var since = clock.UtcNow.AddDays(-30);

        var orders = await db.Orders.AsNoTracking()
            .Where(o => whIds.Contains(o.WarehouseId) && o.CreatedAt >= since && o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);
        var picks = new Dictionary<string, int>();
        var pairs = new Dictionary<string, Dictionary<string, int>>();
        foreach (var o in orders)
        {
            foreach (var line in o.Lines) picks[line.ProductId] = picks.GetValueOrDefault(line.ProductId) + 1;
            foreach (var a in o.Lines)
                foreach (var b in o.Lines)
                {
                    if (a.ProductId == b.ProductId) continue;
                    if (!pairs.TryGetValue(a.ProductId, out var m)) pairs[a.ProductId] = m = [];
                    m[b.ProductId] = m.GetValueOrDefault(b.ProductId) + 1;
                }
        }
        var stock = await db.Stock.AsNoTracking().Where(s => whIds.Contains(s.WarehouseId) && s.Quantity > 0).OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking().Where(l => whIds.Contains(l.WarehouseId)).OrderBy(l => EF.Property<long>(l, "Seq")).ToListAsync(ct);
        var locs = locations.ToDictionary(l => l.Id);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var orgNames = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var items = stock.GroupBy(s => s.ProductId).Select(g => g.First())
            .Select(s => (Stock: s, Picks: picks.GetValueOrDefault(s.ProductId)))
            .OrderByDescending(i => i.Picks).ToList();
        var cls = SlottingPolicy.ClassifyAbc(items.Select(i => (i.Stock.ProductId, i.Picks)));
        var totalPicks = Math.Max(1, items.Sum(i => i.Picks));

        var usedByCell = stock.GroupBy(s => s.LocationId).ToDictionary(g => g.Key, g =>
            SlottingPolicy.UsedLiters(g.Select(s => (products.GetValueOrDefault(s.ProductId)?.VolumeLiters ?? 0, s.Quantity))));
        var skuByCell = stock.GroupBy(s => s.LocationId).ToDictionary(g => g.Key, g => g.Select(s => s.ProductId).ToHashSet());
        var applied = (await db.SlottingApplied.AsNoTracking().Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        var moves = new List<SlottingMove>();
        double current = 0, projected = 0;
        var usedFree = new HashSet<string>();
        var placed = new Dictionary<string, StorageLocation>();
        var useAffinity = algorithm == SlottingAlgorithm.AbcAffinity;

        foreach (var it in items)
        {
            if (!locs.TryGetValue(it.Stock.LocationId, out var from)) continue;
            var d = Distance(from);
            current += it.Picks * d * 2;
            var abc = cls.GetValueOrDefault(it.Stock.ProductId, "C");
            var product = products.GetValueOrDefault(it.Stock.ProductId);
            var needed = (product?.VolumeLiters ?? 0) * it.Stock.Quantity;
            StorageLocation? target = null;
            if (abc == "A" && d > 12)
            {
                var partner = useAffinity && pairs.TryGetValue(it.Stock.ProductId, out var pm)
                    ? pm.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(placed.ContainsKey)
                    : null;
                var anchor = partner is null ? null : placed[partner];
                bool FitsCell(StorageLocation l)
                {
                    if (l.WarehouseId != from.WarehouseId || l.Id == from.Id || usedFree.Contains(l.Id)) return false;
                    var occupants = skuByCell.GetValueOrDefault(l.Id);
                    if (occupants is { Count: > 0 } && occupants.Any(id => id != it.Stock.ProductId)) return false;
                    var used = usedByCell.GetValueOrDefault(l.Id);
                    return SlottingPolicy.Fits(l.CapacityLiters, used, needed) && Distance(l) < d - SlottingPolicy.MinDistanceGain;
                }
                var candidates = locations.Where(FitsCell);
                target = candidates
                    .OrderBy(l => SlottingPolicy.CandidateScore(Distance(l), l.Level, product?.WeightG ?? 0,
                        anchor is null ? 0 : Math.Abs(l.X - anchor.X) + Math.Abs(l.Y - anchor.Y), useAffinity && anchor is not null))
                    .ThenBy(Distance)
                    .FirstOrDefault();
                if (target is not null)
                {
                    usedFree.Add(target.Id);
                    var id = $"slot_{algoName}_{it.Stock.Id}";
                    var gain = Forecasting.Round(it.Picks * (d - Distance(target)) * 2 * (1 + (anchor is null ? 0 : 0.12)));
                    moves.Add(new SlottingMove(id, it.Stock.ProductId, product?.Sku ?? string.Empty, product?.Name ?? string.Empty,
                        orgNames.GetValueOrDefault(it.Stock.SellerId) ?? string.Empty, abc, it.Picks,
                        partner is null ? null : products.GetValueOrDefault(partner)?.Sku, from.Code, d, target.Code, Distance(target), gain, applied.Contains(id),
                        product?.VolumeLiters ?? 0, product?.WeightG ?? 0, target.CapacityLiters));
                }
            }
            var final = target ?? from;
            placed[it.Stock.ProductId] = final;
            projected += it.Picks * Distance(final) * 2;
        }
        projected = Math.Min(projected, current - moves.Sum(m => m.Gain));
        var classes = new[] { "A", "B", "C" }.ToDictionary(c => c, c =>
        {
            var list = items.Where(i => cls.GetValueOrDefault(i.Stock.ProductId, "C") == c).ToList();
            return new ClassShare(list.Count, Forecasting.Round(list.Sum(i => i.Picks) * 100.0 / totalPicks));
        });
        return new SlottingResult(Meta(algoName, seed, sw, version: "2.0.0"), classes, Forecasting.Round(current), Forecasting.Round(projected),
            current > 0 ? Forecasting.Round((current - projected) / current * 1000) / 10.0 : 0,
            moves.OrderByDescending(m => m.Gain).Take(20).ToList());
    }

    public async Task<ApplyResult> ApplyAsync(RequestContext ctx, ApplyInput input, CancellationToken ct)
    {
        var ids = (input.Ids ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        if (ids.Count == 0) throw AppException.Unprocessable("Не выбраны рекомендации", "ids");
        var now = clock.UtcNow;
        foreach (var id in ids)
            await TryApplyMoveAsync(ctx, id, ct);
        var existing = await db.SlottingApplied.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var id in ids)
            if (existing.TryGetValue(id, out var row)) row.Reapply(now);
            else db.SlottingApplied.Add(new SlottingApplied(id, now));
        await uow.SaveChangesAsync(ct);
        return new ApplyResult(ids.Count);
    }

    private async Task TryApplyMoveAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        SlottingAlgorithm algorithm;
        string stockId;
        if (id.StartsWith("slot_abc_affinity_", StringComparison.Ordinal))
        {
            algorithm = SlottingAlgorithm.AbcAffinity;
            stockId = id["slot_abc_affinity_".Length..];
        }
        else if (id.StartsWith("slot_abc_", StringComparison.Ordinal))
        {
            algorithm = SlottingAlgorithm.Abc;
            stockId = id["slot_abc_".Length..];
        }
        else return;

        var source = await db.Stock.FirstOrDefaultAsync(s => s.Id == stockId, ct);
        if (source is null || source.Free <= 0) return;
        var visible = await scope.VisibleWarehouseIdsAsync(ctx, ct);
        if (visible is not null && !visible.Contains(source.WarehouseId)) return;

        SlottingResult planned;
        try { planned = await SlottingAsync(ctx, source.WarehouseId, algorithm, ct); }
        catch (AppException) { return; }
        var move = planned.Moves.FirstOrDefault(m => m.Id == id);
        if (move is null) return;
        var target = await db.Locations.FirstOrDefaultAsync(l => l.WarehouseId == source.WarehouseId && l.Code == move.ToCell, ct);
        if (target is null || target.Id == source.LocationId) return;

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == source.ProductId, ct);
        var occupants = await db.Stock.Where(s => s.LocationId == target.Id).ToListAsync(ct);
        if (occupants.Any(s => s.ProductId != source.ProductId)) return;
        var used = 0.0;
        foreach (var row in occupants)
        {
            var vol = (await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == row.ProductId, ct))?.VolumeLiters ?? 0;
            used += vol * row.Quantity;
        }
        var qty = source.Free;
        if (!SlottingPolicy.Fits(target.CapacityLiters, used, (product?.VolumeLiters ?? 0) * qty)) return;

        source.Take(qty);
        var existing = occupants.FirstOrDefault(s => s.ProductId == source.ProductId && s.SellerId == source.SellerId);
        if (existing is not null) existing.Receive(qty);
        else db.Stock.Add(new StockItem($"stk_{source.ProductId}_{target.Id}_{nowSuffix()}", source.ProductId, source.SellerId,
            source.WarehouseId, target.Id, qty, 0));
        if (source.IsEmpty) db.Stock.Remove(source);

        string nowSuffix() => clock.UtcNow.ToUnixTimeMilliseconds().ToString();
    }

    public async Task<KpiReport> KpiAsync(RequestContext ctx, string? warehouseId, string? sellerId, int? periodParam, CancellationToken ct)
    {
        var period = Math.Clamp(periodParam ?? 30, 1, Forecasting.HistoryDays);
        var warehouses = await WarehousesAsync(ctx, warehouseId, ct);
        var whIds = warehouses.Select(w => w.Id).ToList();
        if (string.IsNullOrEmpty(sellerId)) sellerId = null;
        var seed = SeededRandom.Hash(string.Join(',', whIds) + (sellerId ?? string.Empty));
        var rng = new Mulberry32(seed);
        var scale = Math.Max(1, warehouses.Count) * 140 * (sellerId is null ? 1 : 0.3);
        var hist = Forecasting.History(seed, scale, Today).TakeLast(period).ToList();
        var switchAt = hist.Count / 2;
        var series = hist.Select((h, i) =>
        {
            var after = i >= switchAt;
            var baseline = Forecasting.Round(rng.Float(180, 230));
            var optimized = after ? Forecasting.Round(baseline * rng.Float(0.68, 0.78)) : baseline;
            var hours = Forecasting.Round1(rng.Float(after ? 3.1 : 4.2, after ? 4.4 : 5.8));
            var utilization = Forecasting.Round(rng.Float(62, 91));
            var errors = rng.Int(0, Math.Max(1, Forecasting.Round(h.Value / 120.0)));
            return new KpiPoint(h.Date, h.Value, hours, baseline, optimized, utilization, errors);
        }).ToList();
        var orders = series.Sum(p => p.Orders);
        var avgBase = series.Average(p => p.RouteBaseline);
        var avgOpt = series.Average(p => p.RouteOptimized);
        var contracts = await db.Contracts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var orgNames = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var bySeller = contracts.Where(c => c.WarehouseIds.Any(whIds.Contains)).Select(c => c.SellerId).Distinct()
            .Where(id => sellerId is null || id == sellerId)
            .Select(id => new SellerKpi(id, orgNames.GetValueOrDefault(id) ?? id, Forecasting.Round(orders * rng.Float(0.08, 0.35)),
                Forecasting.Round1(rng.Float(3, 5.5)), Forecasting.Round1(rng.Float(0.1, 1.4))))
            .ToList().OrderByDescending(s => s.Orders).ToList();
        var totals = new KpiTotals(orders, Forecasting.Round(orders / (double)series.Count), Forecasting.Round1(series.Average(p => p.ProcessingHours)),
            Forecasting.Round(avgBase), Forecasting.Round(avgOpt), Forecasting.Round((1 - avgOpt / avgBase) * 1000) / 10.0,
            Forecasting.Round(series.Average(p => p.Utilization)), Forecasting.Round(series.Sum(p => p.Errors) / (double)Math.Max(1, orders) * 1000) / 10.0);
        return new KpiReport(period, totals, series, bySeller);
    }
}
