using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Orders;

public record OrderListItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string? ExternalNumber { get; init; }
    public SalesChannel Channel { get; init; }
    public OrderStatus Status { get; init; }
    public string SellerId { get; init; } = string.Empty;
    public string SellerName { get; init; } = string.Empty;
    public string WarehouseId { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ShipBy { get; init; }
    public DateTimeOffset? ShippedAt { get; init; }
    public int LinesCount { get; init; }
    public int UnitsCount { get; init; }
    public double Amount { get; init; }
    public string DeliveryCity { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }
}

public sealed record OrderLineDto(string ProductId, string Sku, string Name, string Barcode, int Quantity, double Price);

public sealed record OrderDto : OrderListItemDto
{
    public IReadOnlyList<OrderLineDto> Lines { get; init; } = [];
}

public sealed record OrderSummary(int Total, int Overdue, IReadOnlyDictionary<string, int> ByStatus);

/// <summary>Фильтры списка. Status — значение OrderStatus или «overdue»; неизвестные значения игнорируются.</summary>
public sealed record OrderQuery(PageQuery Page, string? Status, SalesChannel? Channel, string? SellerId, string? WarehouseId);

/// <summary>Подписи статусов (ORDER_STATUSES во фронте) — для подзаголовков поиска.</summary>
public static class OrderStatusLabels
{
    public static string Of(OrderStatus status) => status switch
    {
        OrderStatus.New => "Новый",
        OrderStatus.Reserved => "В резерве",
        OrderStatus.OutOfStock => "Нет в наличии",
        OrderStatus.Picking => "В отборе",
        OrderStatus.Picked => "Отобран",
        OrderStatus.Packed => "Упакован",
        OrderStatus.Shipped => "Отгружен",
        _ => "Отменён",
    };
}

/// <summary>Видимость заказов: селлер — свои, ФЦ — на своих складах, администратор — все (scopedOrders/canSee).</summary>
public sealed class OrderScope(IAppDbContext db, WarehouseScope warehouses)
{
    public async Task<IQueryable<Order>> QueryAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(o => o.WarehouseId == warehouseId);
        if (ctx.IsSeller) return query.Where(o => o.SellerId == ctx.OrganizationId);
        if (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible) query = query.Where(o => visible.Contains(o.WarehouseId));
        return query;
    }

    public async Task<IReadOnlyList<Order>> VisibleAsync(RequestContext ctx, string? warehouseId, CancellationToken ct) =>
        await (await QueryAsync(ctx, warehouseId, ct)).OrderBy(o => EF.Property<long>(o, "Seq")).ToListAsync(ct);

    public async Task<bool> CanSeeAsync(RequestContext ctx, Order order, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return true;
        if (ctx.IsSeller) return order.SellerId == ctx.OrganizationId;
        return (await warehouses.VisibleWarehouseIdsAsync(ctx, ct))?.Contains(order.WarehouseId) ?? true;
    }
}

/// <summary>Проекция заказов для API (toOrderListItem/toOrder в моках).</summary>
internal sealed record OrderLookups(IReadOnlyDictionary<string, string> OrgNames, IReadOnlyDictionary<string, string> WarehouseNames,
    IReadOnlyDictionary<string, Product> Products)
{
    public static async Task<OrderLookups> LoadAsync(IAppDbContext db, bool withProducts, CancellationToken ct) => new(
        await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct),
        await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct),
        withProducts ? await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct) : new Dictionary<string, Product>());

    public OrderListItemDto ToListItem(Order o, DateTimeOffset now) => Fill(new OrderListItemDto(), o, now);

    public OrderDto ToDetails(Order o, DateTimeOffset now) => Fill(new OrderDto
    {
        Lines = o.Lines.Select(l =>
        {
            var p = Products.GetValueOrDefault(l.ProductId);
            return new OrderLineDto(l.ProductId, p?.Sku ?? "—", p?.Name ?? "Товар удалён", p?.Barcode ?? string.Empty, l.Quantity, l.Price);
        }).ToList(),
    }, o, now);

    private T Fill<T>(T dto, Order o, DateTimeOffset now) where T : OrderListItemDto => dto with
    {
        Id = o.Id, Number = o.Number, ExternalNumber = o.ExternalNumber, Channel = o.Channel, Status = o.Status,
        SellerId = o.SellerId, SellerName = OrgNames.GetValueOrDefault(o.SellerId) ?? "—",
        WarehouseId = o.WarehouseId, WarehouseName = WarehouseNames.GetValueOrDefault(o.WarehouseId) ?? "—",
        CreatedAt = o.CreatedAt, ShipBy = o.ShipBy, ShippedAt = o.ShippedAt, LinesCount = o.Lines.Count, UnitsCount = o.Units,
        Amount = o.Amount, DeliveryCity = o.DeliveryCity, IsOverdue = o.IsOverdue(now),
    };
}

public sealed class OrderQueryHandler(IAppDbContext db, OrderScope scope, IClock clock)
{
    public const string Overdue = "overdue";

    public async Task<Paged<OrderListItemDto>> ListAsync(RequestContext ctx, OrderQuery query, CancellationToken ct)
    {
        var items = await FilteredAsync(ctx, query, withStatus: true, ct);
        return Paged<OrderListItemDto>.From(items.SortBy(SortKey(query.Page.SortField ?? "createdAt"), query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }

    public async Task<OrderSummary> SummaryAsync(RequestContext ctx, OrderQuery query, CancellationToken ct)
    {
        var items = await FilteredAsync(ctx, query, withStatus: false, ct);
        var byStatus = new Dictionary<string, int>();
        foreach (var item in items)
        {
            var key = SnakeCaseName.Of(item.Status);
            byStatus[key] = byStatus.GetValueOrDefault(key) + 1;
        }
        return new OrderSummary(items.Count, items.Count(i => i.IsOverdue), byStatus);
    }

    public async Task<OrderDto> DetailsAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null || !await scope.CanSeeAsync(ctx, order, ct)) throw AppException.NotFound("Заказ не найден");
        return (await OrderLookups.LoadAsync(db, withProducts: true, ct)).ToDetails(order, clock.UtcNow);
    }

    /// <summary>Статус-фильтр: «overdue» — просроченные, значение OrderStatus — точное совпадение, иное — без фильтра.</summary>
    public static Func<OrderListItemDto, bool> StatusFilter(string? status)
    {
        if (string.IsNullOrEmpty(status)) return _ => true;
        if (status == Overdue) return o => o.IsOverdue;
        return SnakeCaseName.TryParse<OrderStatus>(status, out var parsed) ? o => o.Status == parsed : _ => true;
    }

    private async Task<IReadOnlyList<OrderListItemDto>> FilteredAsync(RequestContext ctx, OrderQuery query, bool withStatus, CancellationToken ct)
    {
        var lookups = await OrderLookups.LoadAsync(db, withProducts: false, ct);
        var now = clock.UtcNow;
        var term = query.Page.Term;
        var status = withStatus ? StatusFilter(query.Status) : _ => true;
        var q = await scope.QueryAsync(ctx, query.WarehouseId, ct);
        if (query.Channel is { } channel) q = q.Where(o => o.Channel == channel);
        if (!string.IsNullOrEmpty(query.SellerId)) q = q.Where(o => o.SellerId == query.SellerId);
        var orders = await q.OrderBy(o => EF.Property<long>(o, "Seq")).ToListAsync(ct);
        return orders.Select(o => lookups.ToListItem(o, now))
            .Where(status)
            .Where(o => term.Length == 0 || o.Number.ToLowerInvariant().Contains(term) ||
                        (o.ExternalNumber?.ToLowerInvariant().Contains(term) ?? false) ||
                        o.DeliveryCity.ToLowerInvariant().Contains(term) || o.SellerName.ToLowerInvariant().Contains(term))
            .ToList();
    }

    private static Func<OrderListItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => o => o.Id,
        "number" => o => o.Number,
        "externalNumber" => o => o.ExternalNumber,
        "channel" => o => SnakeCaseName.Of(o.Channel),
        "status" => o => SnakeCaseName.Of(o.Status),
        "sellerId" => o => o.SellerId,
        "sellerName" => o => o.SellerName,
        "warehouseId" => o => o.WarehouseId,
        "warehouseName" => o => o.WarehouseName,
        "createdAt" => o => o.CreatedAt,
        "shipBy" => o => o.ShipBy,
        "shippedAt" => o => o.ShippedAt,
        "linesCount" => o => o.LinesCount,
        "unitsCount" => o => o.UnitsCount,
        "amount" => o => o.Amount,
        "deliveryCity" => o => o.DeliveryCity,
        "isOverdue" => o => o.IsOverdue,
        _ => null,
    };
}

/// <summary>Продажи товаров за 30 дней (неотменённые заказы), видимые организации — для запаса в днях.</summary>
public sealed class OrderSales(OrderScope scope)
{
    public const int WindowDays = 30;

    public async Task<Dictionary<string, int>> ByProductAsync(RequestContext ctx, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-WindowDays);
        var orders = await (await scope.QueryAsync(ctx, null, ct))
            .Where(o => o.CreatedAt >= since && o.Status != OrderStatus.Cancelled).ToListAsync(ct);
        return Aggregate(orders);
    }

    public static Dictionary<string, int> Aggregate(IEnumerable<Order> orders)
    {
        var sales = new Dictionary<string, int>();
        foreach (var line in orders.Where(o => o.CountsAsSale).SelectMany(o => o.Lines))
            sales[line.ProductId] = sales.GetValueOrDefault(line.ProductId) + line.Quantity;
        return sales;
    }
}

/// <summary>Заказы в глобальном поиске: до 5 совпадений по номеру или внешнему номеру; идут первыми.</summary>
public sealed class OrderSearchProvider(IAppDbContext db, OrderScope scope) : ISearchProvider
{
    public const int Limit = 5;
    public int Order => 10;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(RequestContext ctx, string term, CancellationToken ct)
    {
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return (await scope.VisibleAsync(ctx, null, ct))
            .Where(o => o.Number.ToLowerInvariant().Contains(term) || (o.ExternalNumber?.ToLowerInvariant().Contains(term) ?? false))
            .Take(Limit)
            .Select(o => new SearchResult("order", o.Id, o.Number,
                $"{OrderStatusLabels.Of(o.Status)} · {names.GetValueOrDefault(o.SellerId)} · {o.ExternalNumber ?? string.Empty}"))
            .ToList();
    }
}

/// <summary>ФЦ — уведомление о просроченных заказах (одно в сутки).</summary>
public sealed class OverdueOrdersNotificationSource(OrderScope scope) : INotificationSource
{
    public async Task<IReadOnlyList<NotificationItem>> GetAsync(RequestContext ctx, DateTimeOffset now, CancellationToken ct)
    {
        if (!ctx.IsFulfillment) return [];
        var overdue = (await scope.VisibleAsync(ctx, null, ct)).Count(o => o.IsOverdue(now));
        if (overdue == 0) return [];
        return [new NotificationItem($"overdue_{now.UtcDateTime:yyyy-MM-dd}", "Есть просроченные заказы",
            $"{overdue} заказов не отгружены в срок", now.AddMinutes(-20), AttentionTone.Danger, null)];
    }
}

/// <summary>Журнал платформы: отгрузки заказов за последние 7 дней.</summary>
public sealed class ShippedOrdersAuditSource(IAppDbContext db) : IAuditEventSource
{
    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-7);
        var owners = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.OrganizationId, ct);
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var orders = await db.Orders.AsNoTracking().Where(o => o.ShippedAt != null && o.ShippedAt >= since)
            .OrderBy(o => EF.Property<long>(o, "Seq")).ToListAsync(ct);
        return orders.Select(o =>
        {
            var fc = owners.GetValueOrDefault(o.WarehouseId);
            return new AuditEventDto($"aud_ship_{o.Id}", o.ShippedAt!.Value, AuditCategory.Order, "Заказ отгружен",
                $"{o.Lines.Count} строк, {o.DeliveryCity}", "Смена отгрузки", fc, fc is null ? null : names.GetValueOrDefault(fc), o.Number);
        }).ToList();
    }
}
