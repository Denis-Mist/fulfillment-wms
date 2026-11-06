using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;

namespace Wms.Application.Tasks;

public sealed record PickCandidateDto(string Id, string Number, SalesChannel Channel, string SellerName, string WarehouseId,
    DateTimeOffset ShipBy, bool IsOverdue, int LinesCount, int Units);

public sealed record RoutePointDto(string Code, double X, double Y);
public sealed record RouteVariantDto(double Distance, IReadOnlyList<RoutePointDto> Points);
public sealed record RoutePreviewDto(RouteVariantDto Optimized, RouteVariantDto Baseline, int StepsCount, int Units, int EstimatedMinutes,
    double VolumeLiters = 0, double ToteCapacityLiters = 0, double ToteFill = 0);

public sealed record BatchSuggestionDto(IReadOnlyList<string> OrderIds, IReadOnlyList<string> OrderNumbers,
    double VolumeLiters, double ToteCapacityLiters, double ToteFill, RoutePreviewDto Route);

public sealed record BatchSuggestionsDto(double ToteCapacityLiters, IReadOnlyList<BatchSuggestionDto> Batches);

public sealed record AutoBatchResult(int Created, IReadOnlyList<TaskDetailsDto> Tasks);

/// <summary>Комплектация: кандидаты в батч, предпросмотр маршрута и формирование листа отбора.</summary>
public sealed class PickingHandler(IAppDbContext db, IUnitOfWork uow, WarehouseScope warehouses, IIdGenerator ids, IClock clock)
{
    public async Task<IReadOnlyList<PickCandidateDto>> CandidatesAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var orders = await CandidateOrdersAsync(ctx, warehouseId, ct, tracking: false);
        var sellerIds = orders.Select(o => o.SellerId).Distinct().ToList();
        var sellers = await db.Organizations.AsNoTracking().Where(o => sellerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return orders.OrderBy(o => o.ShipBy).Select(o => new PickCandidateDto(o.Id, o.Number, o.Channel, sellers.GetValueOrDefault(o.SellerId) ?? string.Empty,
            o.WarehouseId, o.ShipBy, o.ShipBy < now, o.Lines.Count, o.Units)).ToList();
    }

    public async Task<RoutePreviewDto> PreviewAsync(RequestContext ctx, IReadOnlyList<string> orderIds, CancellationToken ct)
    {
        var pool = (await CandidateOrdersAsync(ctx, null, ct, tracking: false)).ToDictionary(o => o.Id);
        var orders = orderIds.Where(pool.ContainsKey).Select(id => pool[id]).ToList();
        var pick = await PickContext.LoadAsync(db, orders.Select(o => o.WarehouseId).Distinct().ToList(), ct);
        var volumes = await VolumesAsync(orders, ct);
        return Preview(pick.PickItems(orders), pick.Nodes, OrderVolume(orders, volumes));
    }

    public static RoutePreviewDto Preview(IReadOnlyList<TaskItem> items, IReadOnlyDictionary<string, RouteNode> nodes,
        double volumeLiters = 0, double toteCapacityLiters = BatchOptimizer.DefaultToteLiters)
    {
        var points = items.Where(i => nodes.ContainsKey(i.LocationId)).Select(i => nodes[i.LocationId]).ToList();
        var optimized = RoutePlanner.Optimized(points, p => p);
        var baseline = RoutePlanner.Baseline(points, p => p);
        var units = items.Sum(i => i.Quantity);
        var distance = RoutePlanner.Length(optimized);
        return new RoutePreviewDto(Variant(optimized), Variant(baseline), items.Count, units,
            RoutePlanner.EstimatedMinutes(distance, units, items.Count),
            JsMath.Round1(volumeLiters), toteCapacityLiters, BatchOptimizer.ToteFill(volumeLiters, toteCapacityLiters));
        static RouteVariantDto Variant(IReadOnlyList<RouteNode> route) =>
            new(RoutePlanner.Length(route), route.Select(p => new RoutePointDto(p.Code, p.X, p.Y)).ToList());
    }

    public async Task<BatchSuggestionsDto> SuggestAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var groups = await PlanGroupsAsync(ctx, warehouseId, tracking: false, ct);
        var batches = new List<BatchSuggestionDto>();
        foreach (var group in groups)
        {
            foreach (var plan in group.Plans)
            {
                var orders = plan.OrderIds.Select(id => group.ById[id]).ToList();
                var preview = Preview(group.Pick.PickItems(orders), group.Pick.Nodes, plan.VolumeLiters, plan.ToteCapacityLiters);
                batches.Add(new BatchSuggestionDto(plan.OrderIds, orders.Select(o => o.Number).ToList(),
                    plan.VolumeLiters, plan.ToteCapacityLiters, BatchOptimizer.ToteFill(plan.VolumeLiters, plan.ToteCapacityLiters), preview));
            }
        }
        return new BatchSuggestionsDto(BatchOptimizer.DefaultToteLiters, batches);
    }

    public async Task<AutoBatchResult> CreateAutoBatchesAsync(RequestContext ctx, string? warehouseId, string? algorithm, CancellationToken ct)
    {
        var route = RouteAlgorithms.Parse(algorithm);
        var groups = await PlanGroupsAsync(ctx, warehouseId, tracking: true, ct);
        if (groups.Count == 0) return new AutoBatchResult(0, []);

        var created = new List<TaskDetailsDto>();
        var n = await db.Tasks.CountAsync(ct);
        var now = clock.UtcNow;
        foreach (var group in groups)
        {
            foreach (var plan in group.Plans)
            {
                var orders = plan.OrderIds.Select(id => group.ById[id]).ToList();
                created.Add(await BuildBatchAsync(ctx, orders, route, group.Pick, group.Volumes, now, ++n, ct));
            }
        }
        await uow.SaveChangesAsync(ct);
        return new AutoBatchResult(created.Count, created);
    }

    public async Task<TaskDetailsDto> CreateBatchAsync(RequestContext ctx, IReadOnlyList<string> orderIds, string? algorithm, CancellationToken ct)
    {
        if (orderIds.Count == 0)
            throw AppException.Unprocessable("Выберите заказы", "Отметьте хотя бы один заказ для формирования батча.");
        var pool = (await CandidateOrdersAsync(ctx, null, ct, tracking: true)).ToDictionary(o => o.Id);
        var orders = orderIds.Where(pool.ContainsKey).Select(id => pool[id]).ToList();
        if (orders.Count == 0 || orders.Count != orderIds.Count)
            throw AppException.Conflict("Часть заказов уже в работе", "Обновите список заказов и сформируйте батч заново.");
        if (orders.Select(o => o.WarehouseId).Distinct().Count() > 1)
            throw AppException.Unprocessable("Заказы с разных складов", "Батч формируется в пределах одного склада.");
        if (orders.Count > PickListBuilder.MaxBatchOrders)
            throw AppException.Unprocessable("Слишком большой батч", "Не более 12 заказов в одном листе отбора.");

        var route = RouteAlgorithms.Parse(algorithm);
        var now = clock.UtcNow;
        var pick = await PickContext.LoadAsync(db, [orders[0].WarehouseId], ct);
        var volumes = await VolumesAsync(orders, ct);
        var n = await db.Tasks.CountAsync(ct) + 1;
        var details = await BuildBatchAsync(ctx, orders, route, pick, volumes, now, n, ct);
        await uow.SaveChangesAsync(ct);
        return details;
    }

    private async Task<TaskDetailsDto> BuildBatchAsync(RequestContext ctx, List<Order> orders, string route, PickContext pick,
        IReadOnlyDictionary<string, double> volumes, DateTimeOffset now, int n, CancellationToken ct)
    {
        var items = RoutePlanner.Sort(pick.PickItems(orders), pick.Nodes, route);
        var preview = Preview(items, pick.Nodes, OrderVolume(orders, volumes));
        var chosenDistance = route == RouteAlgorithms.Baseline ? preview.Baseline.Distance : preview.Optimized.Distance;
        var minutes = RoutePlanner.EstimatedMinutes(chosenDistance, preview.Units, items.Count);
        var task = WarehouseTask.PickingBatch($"tsk_b{ids.NewSuffix()}", "T-" + (100000L + n * 7 + 3).ToString(System.Globalization.CultureInfo.InvariantCulture)[^6..],
            orders[0].WarehouseId, ctx.OrganizationId!, now, TaskRules.PriorityFor(orders, now), orders.Select(o => o.Id), route, items);
        task.RecordMetrics(preview.Optimized.Distance, preview.Baseline.Distance, minutes, preview.VolumeLiters, preview.ToteCapacityLiters);
        db.Tasks.Add(task);
        try { foreach (var o in orders) o.StartPicking(); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        return (await TaskLookups.LoadAsync(db, [task], true, ct)).ToDetails(task, now);
    }

    /// <summary>Заказы в резерве, не входящие в неотменённые задания, на видимых складах.</summary>
    private async Task<List<Order>> CandidateOrdersAsync(RequestContext ctx, string? warehouseId, CancellationToken ct, bool tracking)
    {
        var busy = (await db.Tasks.AsNoTracking().Where(t => t.Status != WarehouseTaskStatus.Cancelled).Select(t => t.OrderIds).ToListAsync(ct))
            .SelectMany(x => x).ToHashSet();
        var query = (tracking ? db.Orders : db.Orders.AsNoTracking()).Where(o => o.Status == OrderStatus.Reserved);
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(o => o.WarehouseId == warehouseId);
        if (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible) query = query.Where(o => visible.Contains(o.WarehouseId));
        return (await query.OrderBy(o => EF.Property<long>(o, "Seq")).ToListAsync(ct)).Where(o => !busy.Contains(o.Id)).ToList();
    }

    private async Task<List<WarehousePlan>> PlanGroupsAsync(RequestContext ctx, string? warehouseId, bool tracking, CancellationToken ct)
    {
        var orders = await CandidateOrdersAsync(ctx, warehouseId, ct, tracking);
        if (orders.Count == 0) return [];
        var volumes = await VolumesAsync(orders, ct);
        var groups = new List<WarehousePlan>();
        foreach (var group in orders.GroupBy(o => o.WarehouseId))
        {
            var pick = await PickContext.LoadAsync(db, [group.Key], ct);
            var candidates = group.Select(o =>
            {
                var items = pick.PickItems([o]);
                var stops = items.Where(i => pick.Nodes.ContainsKey(i.LocationId)).Select(i => pick.Nodes[i.LocationId]).ToList();
                return new BatchCandidate(o.Id, o.ShipBy, OrderVolume([o], volumes), stops);
            }).ToList();
            groups.Add(new WarehousePlan(group.ToDictionary(o => o.Id), pick, volumes, BatchOptimizer.Plan(candidates)));
        }
        return groups;
    }

    private async Task<Dictionary<string, double>> VolumesAsync(IEnumerable<Order> orders, CancellationToken ct)
    {
        var ids = orders.SelectMany(o => o.Lines.Select(l => l.ProductId)).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.VolumeLiters, ct);
    }

    private static double OrderVolume(IEnumerable<Order> orders, IReadOnlyDictionary<string, double> volumes) =>
        orders.SelectMany(o => o.Lines).Sum(l => l.Quantity * volumes.GetValueOrDefault(l.ProductId));

    private sealed record WarehousePlan(
        Dictionary<string, Order> ById,
        PickContext Pick,
        IReadOnlyDictionary<string, double> Volumes,
        IReadOnlyList<BatchPlan> Plans);
}
