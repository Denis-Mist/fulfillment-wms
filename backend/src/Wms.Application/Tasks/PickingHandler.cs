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
public sealed record RoutePreviewDto(RouteVariantDto Optimized, RouteVariantDto Baseline, int StepsCount, int Units, int EstimatedMinutes);

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
        return Preview(pick.PickItems(orders), pick.Nodes);
    }

    public static RoutePreviewDto Preview(IReadOnlyList<TaskItem> items, IReadOnlyDictionary<string, RouteNode> nodes)
    {
        var points = items.Where(i => nodes.ContainsKey(i.LocationId)).Select(i => nodes[i.LocationId]).ToList();
        var optimized = RoutePlanner.Optimized(points, p => p);
        var baseline = RoutePlanner.Baseline(points, p => p);
        var units = items.Sum(i => i.Quantity);
        var distance = RoutePlanner.Length(optimized);
        return new RoutePreviewDto(Variant(optimized), Variant(baseline), items.Count, units,
            (int)Math.Max(1, JsMath.Round((distance / 1.2 + units * 6 + items.Count * 10) / 60)));
        static RouteVariantDto Variant(IReadOnlyList<RouteNode> route) =>
            new(RoutePlanner.Length(route), route.Select(p => new RoutePointDto(p.Code, p.X, p.Y)).ToList());
    }

    public async Task<TaskDetailsDto> CreateBatchAsync(RequestContext ctx, IReadOnlyList<string> orderIds, string? algorithm, CancellationToken ct)
    {
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
        var items = RoutePlanner.Sort(pick.PickItems(orders), pick.Nodes, route);
        var n = await db.Tasks.CountAsync(ct) + 1;
        var task = WarehouseTask.PickingBatch($"tsk_b{ids.NewSuffix()}", "T-" + (100000L + n * 7 + 3).ToString(System.Globalization.CultureInfo.InvariantCulture)[^6..],
            orders[0].WarehouseId, ctx.OrganizationId!, now, TaskRules.PriorityFor(orders, now), orders.Select(o => o.Id), route, items);
        db.Tasks.Add(task);
        try { foreach (var o in orders) o.StartPicking(); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        await uow.SaveChangesAsync(ct);
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
}
