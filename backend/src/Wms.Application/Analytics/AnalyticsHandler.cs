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
public sealed record WarehouseKpi(string WarehouseId, string Name, int Orders, double AvgProcessingHours, double ErrorRate);
public sealed record KpiReport(int Period, KpiTotals Totals, IReadOnlyList<KpiPoint> Series, IReadOnlyList<SellerKpi> BySeller,
    IReadOnlyList<WarehouseKpi> ByWarehouse);

/// <summary>Аналитика ФЦ: прогноз и план персонала, слоттинг ABC, KPI по реальным заказам и маршрутам.</summary>
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

    private static T ResolveAlgorithm<T>(string? raw, T fallback) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(raw)) return fallback;
        if (SnakeCaseName.TryParse(raw, out T value)) return value;
        throw AppException.Unprocessable("Неизвестный алгоритм", "algorithm");
    }

    public async Task<ForecastResult> ForecastAsync(RequestContext ctx, string? warehouseId, int? horizonParam, string? algorithmParam, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var horizon = Math.Clamp(horizonParam ?? 14, 7, 28);
        var algorithm = ResolveAlgorithm(algorithmParam, ForecastAlgorithm.HoltWinters);
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
        var algorithm = ResolveAlgorithm(input.Algorithm, ForecastAlgorithm.HoltWinters);
        var key = StaffPlan.KeyFor(ctx.OrganizationId, string.IsNullOrEmpty(input.WarehouseId) ? null : input.WarehouseId);
        var by = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? string.Empty;
        var plan = await db.StaffPlans.FirstOrDefaultAsync(p => p.Id == key, ct);
        if (plan is null) db.StaffPlans.Add(plan = new StaffPlan(key, algorithm, by, clock.UtcNow));
        else plan.Approve(algorithm, by, clock.UtcNow);
        await uow.SaveChangesAsync(ct);
        return new PlanDto(plan.ApprovedAt, plan.ApprovedBy, plan.Algorithm);
    }

    private static double Distance(StorageLocation l) => SlottingPolicy.Distance(l.X, l.Y, l.Level);

    public async Task<SlottingResult> SlottingAsync(RequestContext ctx, string? warehouseId, string? algorithmParam, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var algorithm = ResolveAlgorithm(algorithmParam, SlottingAlgorithm.AbcAffinity);
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
        var appliedIds = new List<string>();
        foreach (var id in ids)
            if (await TryApplyMoveAsync(ctx, id, ct)) appliedIds.Add(id);
        if (appliedIds.Count == 0) return new ApplyResult(0);
        var existing = await db.SlottingApplied.Where(a => appliedIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var id in appliedIds)
            if (existing.TryGetValue(id, out var row)) row.Reapply(now);
            else db.SlottingApplied.Add(new SlottingApplied(id, now));
        await uow.SaveChangesAsync(ct);
        return new ApplyResult(appliedIds.Count);
    }

    private async Task<bool> TryApplyMoveAsync(RequestContext ctx, string id, CancellationToken ct)
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
        else return false;

        var source = await db.Stock.FirstOrDefaultAsync(s => s.Id == stockId, ct);
        if (source is null || source.Free <= 0) return false;
        var visible = await scope.VisibleWarehouseIdsAsync(ctx, ct);
        if (visible is not null && !visible.Contains(source.WarehouseId)) return false;

        SlottingResult planned;
        try { planned = await SlottingAsync(ctx, source.WarehouseId, SnakeCaseName.Of(algorithm), ct); }
        catch (AppException) { return false; }
        var move = planned.Moves.FirstOrDefault(m => m.Id == id);
        if (move is null) return false;
        var target = await db.Locations.FirstOrDefaultAsync(l => l.WarehouseId == source.WarehouseId && l.Code == move.ToCell, ct);
        if (target is null || target.Id == source.LocationId) return false;

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == source.ProductId, ct);
        var occupants = await db.Stock.Where(s => s.LocationId == target.Id).ToListAsync(ct);
        if (occupants.Any(s => s.ProductId != source.ProductId)) return false;
        var used = 0.0;
        foreach (var row in occupants)
        {
            var vol = (await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == row.ProductId, ct))?.VolumeLiters ?? 0;
            used += vol * row.Quantity;
        }
        var qty = source.Free;
        if (!SlottingPolicy.Fits(target.CapacityLiters, used, (product?.VolumeLiters ?? 0) * qty)) return false;

        source.Take(qty);
        var existing = occupants.FirstOrDefault(s => s.ProductId == source.ProductId && s.SellerId == source.SellerId);
        if (existing is not null) existing.Receive(qty);
        else db.Stock.Add(new StockItem($"stk_{source.ProductId}_{target.Id}_{nowSuffix()}", source.ProductId, source.SellerId,
            source.WarehouseId, target.Id, qty, 0));
        if (source.IsEmpty) db.Stock.Remove(source);

        string nowSuffix() => clock.UtcNow.ToUnixTimeMilliseconds().ToString();
        return true;
    }

    public async Task<KpiReport> KpiAsync(RequestContext ctx, string? warehouseId, string? sellerId, int? periodParam, CancellationToken ct)
    {
        var period = Math.Clamp(periodParam ?? 30, 1, Forecasting.HistoryDays);
        var warehouses = await WarehousesAsync(ctx, warehouseId, ct);
        var whIds = warehouses.Select(w => w.Id).ToList();
        if (string.IsNullOrEmpty(sellerId)) sellerId = null;
        var today = Today;
        var from = today.AddDays(-period);

        var orderRows = await db.Orders.AsNoTracking()
            .Where(o => whIds.Contains(o.WarehouseId) && o.Status != OrderStatus.Cancelled
                && (o.CreatedAt >= from && o.CreatedAt < today || o.ShippedAt != null && o.ShippedAt >= from && o.ShippedAt < today))
            .ToListAsync(ct);
        if (sellerId is not null) orderRows = orderRows.Where(o => o.SellerId == sellerId).ToList();
        var created = orderRows.Where(o => InDayRange(o.CreatedAt, from, today)).ToList();
        var sellerOrderIds = orderRows.Select(o => o.Id).ToHashSet();

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => whIds.Contains(t.WarehouseId) && t.Status != WarehouseTaskStatus.Cancelled
                && (t.CreatedAt >= from && t.CreatedAt < today || t.FinishedAt != null && t.FinishedAt >= from && t.FinishedAt < today))
            .ToListAsync(ct);
        if (sellerId is not null)
            tasks = tasks.Where(t => t.OrderIds.Any(sellerOrderIds.Contains)).ToList();

        var locationIds = tasks.SelectMany(t => t.Items.Select(i => i.LocationId)).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => new RouteNode(l.Code, l.X, l.Y), ct);

        var operations = await db.StockOperations.AsNoTracking()
            .Where(o => whIds.Contains(o.WarehouseId) && o.At >= from && o.At < today
                && (o.Type == OperationType.Writeoff || o.Reason != null))
            .ToListAsync(ct);
        var orgNames = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        if (sellerId is not null)
        {
            var sellerName = orgNames.GetValueOrDefault(sellerId) ?? sellerId;
            operations = operations.Where(o => o.SellerName == sellerName).ToList();
        }

        var staff = await db.StaffProfiles.AsNoTracking().Where(p => whIds.Contains(p.WarehouseId)).ToListAsync(ct);
        var routes = tasks.Where(t => t.Type == TaskType.Picking).ToDictionary(t => t.Id, t => RouteOf(t, locations));

        var series = new List<KpiPoint>(period);
        for (var i = 0; i < period; i++)
        {
            var day = from.AddDays(i);
            var next = day.AddDays(1);
            var dayOrders = created.Where(o => InDayRange(o.CreatedAt, day, next)).ToList();
            var hours = CycleHours(orderRows.Where(o => o.ShippedAt is { } shipped && InDayRange(shipped, day, next))
                .Select(o => (o.CreatedAt, o.ShippedAt!.Value)));
            var finished = tasks.Where(t => t.Status == WarehouseTaskStatus.Done && t.FinishedAt is { } done && InDayRange(done, day, next)).ToList();
            if (hours.Count == 0)
                hours = CycleHours(finished.Where(t => t.StartedAt is not null).Select(t => (t.StartedAt!.Value, t.FinishedAt!.Value)));

            var dayRoutes = tasks.Where(t => t.Type == TaskType.Picking && InDayRange(t.FinishedAt ?? t.CreatedAt, day, next))
                .Select(t => routes[t.Id]).ToList();
            var workMinutes = finished.Where(t => t.StartedAt is not null)
                .Sum(t => (t.FinishedAt!.Value - t.StartedAt!.Value).TotalMinutes);
            series.Add(new KpiPoint(day, dayOrders.Count, Forecasting.Round1(KpiMetrics.AverageOrZero(hours)),
                Forecasting.Round(KpiMetrics.AverageOrZero(dayRoutes.Select(r => r.Baseline).ToList())),
                Forecasting.Round(KpiMetrics.AverageOrZero(dayRoutes.Select(r => r.Optimized).ToList())),
                KpiMetrics.Utilization(workMinutes, staff.Count(p => p.WorksOn(WarehouseTime.DayIndex(day)))),
                operations.Count(o => InDayRange(o.At, day, next))));
        }

        var windowRoutes = tasks.Where(t => t.Type == TaskType.Picking && InDayRange(t.FinishedAt ?? t.CreatedAt, from, today))
            .Select(t => routes[t.Id]).ToList();
        var allHours = CycleHours(orderRows.Where(o => o.ShippedAt is { } shipped && InDayRange(shipped, from, today))
            .Select(o => (o.CreatedAt, o.ShippedAt!.Value)));
        if (allHours.Count == 0)
            allHours = CycleHours(tasks.Where(t => t.Status == WarehouseTaskStatus.Done && t.FinishedAt is { } done
                    && InDayRange(done, from, today) && t.StartedAt is not null)
                .Select(t => (t.StartedAt!.Value, t.FinishedAt!.Value)));
        var avgBase = KpiMetrics.AverageOrZero(windowRoutes.Select(r => r.Baseline).ToList());
        var avgOpt = KpiMetrics.AverageOrZero(windowRoutes.Select(r => r.Optimized).ToList());
        var totalOrders = series.Sum(p => p.Orders);
        var totals = new KpiTotals(totalOrders, series.Count == 0 ? 0 : Forecasting.Round(totalOrders / (double)series.Count),
            Forecasting.Round1(KpiMetrics.AverageOrZero(allHours)), Forecasting.Round(avgBase), Forecasting.Round(avgOpt),
            KpiMetrics.ReductionPct(avgBase, avgOpt),
            series.Count == 0 ? 0 : Forecasting.Round(series.Average(p => p.Utilization)),
            KpiMetrics.ErrorRate(operations.Count, totalOrders));

        var bySeller = created.GroupBy(o => o.SellerId)
            .Select(g => Slice(g.Key, orgNames.GetValueOrDefault(g.Key) ?? g.Key, g.ToList(), orderRows, operations, from, today, matchSeller: true))
            .Select(x => new SellerKpi(x.Id, x.Name, x.Orders, x.Hours, x.Errors))
            .OrderByDescending(s => s.Orders).ToList();
        var byWarehouse = warehouses
            .Select(w => Slice(w.Id, w.Name, created.Where(o => o.WarehouseId == w.Id).ToList(),
                orderRows.Where(o => o.WarehouseId == w.Id).ToList(), operations.Where(o => o.WarehouseId == w.Id).ToList(),
                from, today, matchSeller: false))
            .Select(x => new WarehouseKpi(x.Id, x.Name, x.Orders, x.Hours, x.Errors))
            .OrderByDescending(w => w.Orders).ToList();
        return new KpiReport(period, totals, series, bySeller, byWarehouse);
    }

    private static bool InDayRange(DateTimeOffset at, DateTimeOffset from, DateTimeOffset to) => at >= from && at < to;

    private static List<double> CycleHours(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> spans) =>
        spans.Select(s => (s.End - s.Start).TotalHours).Where(h => h >= 0).ToList();

    private static (double Baseline, double Optimized) RouteOf(WarehouseTask task, IReadOnlyDictionary<string, RouteNode> locations)
    {
        if (task.BaselineDistance is { } baseline && task.OptimizedDistance is { } optimized) return (baseline, optimized);
        return KpiMetrics.RoutePair(task.Items.Where(i => locations.ContainsKey(i.LocationId)).Select(i => locations[i.LocationId]));
    }

    private static (string Id, string Name, int Orders, double Hours, double Errors) Slice(string id, string name,
        IReadOnlyCollection<Order> created, IReadOnlyCollection<Order> related, IReadOnlyCollection<StockOperation> errors,
        DateTimeOffset from, DateTimeOffset today, bool matchSeller)
    {
        var hours = CycleHours(related.Where(o => o.ShippedAt is { } shipped && InDayRange(shipped, from, today))
            .Select(o => (o.CreatedAt, o.ShippedAt!.Value)));
        var counted = matchSeller ? errors.Where(o => o.SellerName == name).ToList() : errors.ToList();
        return (id, name, created.Count, Forecasting.Round1(KpiMetrics.AverageOrZero(hours)), KpiMetrics.ErrorRate(counted.Count, created.Count));
    }
}
