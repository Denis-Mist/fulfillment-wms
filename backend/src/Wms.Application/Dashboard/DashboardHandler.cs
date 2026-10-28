using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Inbound;
using Wms.Application.Orders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Dashboard;

public sealed record KpiValue(double Value, double? Previous = null);

public sealed record AttentionItemDto(string Key, string Title, string Description, int Count, string Tone, string Section,
    IReadOnlyDictionary<string, string>? Query = null);

public sealed record FcKpis(KpiValue OrdersToday, KpiValue ShippedToday, KpiValue InProgress, KpiValue Overdue, KpiValue InboundToday, KpiValue Occupancy);
public sealed record PipelineItem(OrderStatus Status, int Count);
public sealed record FcTrendPoint(string Date, int Created, int Shipped);
public sealed record ChannelCount(SalesChannel Channel, int Count);
public sealed record WarehouseLoad(string Id, string Name, int Occupancy, int InProgress);
public sealed record TopClient(string Id, string Name, int Orders, int Units);

public sealed record FcDashboardDto(FcKpis Kpis, IReadOnlyList<PipelineItem> Pipeline, IReadOnlyList<AttentionItemDto> Attention,
    IReadOnlyList<FcTrendPoint> Trend, IReadOnlyList<ChannelCount> Channels, IReadOnlyList<WarehouseLoad> Warehouses, IReadOnlyList<TopClient> TopClients);

public sealed record SellerKpis(KpiValue StockUnits, KpiValue StockValue, KpiValue Orders30d, KpiValue OnTimeRate, KpiValue ChargesMonth);
public sealed record SellerTrendPoint(string Date, int Wb, int Ozon, int Own);
public sealed record FulfillmentStock(string Id, string Name, string City, int Units, int Skus, int Warehouses);
public sealed record LowStockItem(string ProductId, string Sku, string Name, int Available, double DailySales, int DaysLeft);

public sealed record SellerDashboardDto(SellerKpis Kpis, IReadOnlyList<AttentionItemDto> Attention, IReadOnlyList<SellerTrendPoint> Trend,
    IReadOnlyList<FulfillmentStock> Fulfillments, IReadOnlyList<LowStockItem> LowStock, IReadOnlyList<InboundListItemDto> RecentInbound);

/// <summary>Дашборды ФЦ и селлера (mocks/handlers/dashboard.ts). Сутки считаются в UTC.</summary>
public sealed class DashboardHandler(IAppDbContext db, IClock clock, OrderScope orders, InboundScope inbound)
{
    private static readonly OrderStatus[] Pipeline = [OrderStatus.New, OrderStatus.Reserved, OrderStatus.Picking, OrderStatus.Picked, OrderStatus.Packed];

    public static int PeriodDays(string? period) => period switch { "7d" => 7, "90d" => 90, _ => 30 };

    private static string DayKey(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static List<string> Buckets(int days, DateTimeOffset today) =>
        Enumerable.Range(0, days).Select(i => DayKey(today.AddDays(-(days - 1 - i)))).ToList();

    private static bool Between(DateTimeOffset? t, DateTimeOffset from, DateTimeOffset to) => t is { } v && v >= from && v < to;

    private static int Percent(int part, int total) => total == 0 ? 0 : (int)Math.Round(part * 100.0 / total, MidpointRounding.AwayFromZero);

    public async Task<FcDashboardDto> FulfillmentAsync(RequestContext ctx, string? period, string? warehouseId, CancellationToken ct)
    {
        if (!ctx.IsFulfillment) throw AppException.Forbidden("Дашборд доступен только фулфилмент-центрам");
        var now = clock.UtcNow;
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var elapsed = now - today;
        var days = PeriodDays(period);
        if (string.IsNullOrEmpty(warehouseId)) warehouseId = null;
        var list = await orders.VisibleAsync(ctx, null, ct);
        var scoped = list.Where(o => warehouseId is null || o.WarehouseId == warehouseId).ToList();
        var shipments = await inbound.VisibleAsync(ctx, warehouseId, ct);
        var visible = (await db.Warehouses.AsNoTracking().OrderBy(w => EF.Property<long>(w, "Seq")).ToListAsync(ct))
            .Where(w => w.OrganizationId == ctx.OrganizationId).ToList();
        var ids = visible.Select(w => w.Id).ToList();
        var totals = await db.Locations.AsNoTracking().Where(l => ids.Contains(l.WarehouseId)).GroupBy(l => l.WarehouseId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var occupied = await db.Stock.AsNoTracking().Where(s => ids.Contains(s.WarehouseId) && s.Quantity > 0).GroupBy(s => s.WarehouseId)
            .Select(g => new { g.Key, Count = g.Select(s => s.LocationId).Distinct().Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var orgNames = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var active = scoped.Where(o => o.IsActive).ToList();
        var overdue = active.Count(o => o.ShipBy < now);
        var scopeIds = warehouseId is null ? ids : [warehouseId];
        var occTotal = scopeIds.Sum(id => totals.GetValueOrDefault(id));
        var occUsed = scopeIds.Sum(id => occupied.GetValueOrDefault(id));

        var attention = new List<AttentionItemDto>
        {
            new("overdue", "Просроченные заказы", "Срок отгрузки истёк — маркетплейс может оштрафовать", overdue, "danger", "orders", Q("overdue")),
            new("out_of_stock", "Заказы без остатка", "Товара не хватает для резерва", active.Count(o => o.Status == OrderStatus.OutOfStock), "danger", "orders", Q("out_of_stock")),
            new("arrived", "Поставки ждут приёмки", "Машины на складе, приёмка не начата", shipments.Count(i => i.Status == InboundStatus.Arrived), "warn", "inbound", Q("arrived")),
            new("placement", "Ждут размещения", "Товар принят, но ещё не в ячейках", shipments.Count(i => i.Status == InboundStatus.Received), "info", "inbound", Q("received")),
            new("new", "Новые заказы", "Ожидают резервирования", active.Count(o => o.Status == OrderStatus.New), "info", "orders", Q("new")),
        }.Where(a => a.Count > 0).ToList();

        var trend = Buckets(days, today).ToDictionary(k => k, k => (Created: 0, Shipped: 0));
        var periodStart = today.AddDays(-(days - 1));
        var channels = new Dictionary<SalesChannel, int>();
        var clients = new Dictionary<string, (int Orders, int Units)>();
        var clientOrder = new List<string>();
        foreach (var o in scoped)
        {
            if (o.CreatedAt >= periodStart && trend.TryGetValue(DayKey(o.CreatedAt), out var b))
            {
                trend[DayKey(o.CreatedAt)] = (b.Created + 1, b.Shipped);
                channels[o.Channel] = channels.GetValueOrDefault(o.Channel) + 1;
                if (!clients.ContainsKey(o.SellerId)) clientOrder.Add(o.SellerId);
                var c = clients.GetValueOrDefault(o.SellerId);
                clients[o.SellerId] = (c.Orders + 1, c.Units + o.Units);
            }
            if (o.ShippedAt is { } at && at >= periodStart && trend.TryGetValue(DayKey(at), out var s))
                trend[DayKey(at)] = (s.Created, s.Shipped + 1);
        }

        var kpis = new FcKpis(
            new(scoped.Count(o => Between(o.CreatedAt, today, now.AddTicks(1))), scoped.Count(o => Between(o.CreatedAt, today.AddDays(-1), today.AddDays(-1) + elapsed))),
            new(scoped.Count(o => Between(o.ShippedAt, today, now.AddTicks(1))), scoped.Count(o => Between(o.ShippedAt, today.AddDays(-1), today.AddDays(-1) + elapsed))),
            new(active.Count), new(overdue),
            new(shipments.Count(i => i.Status != InboundStatus.Cancelled && Between(i.ExpectedAt, today, today.AddDays(1)))),
            new(Percent(occUsed, occTotal)));

        return new FcDashboardDto(kpis,
            Pipeline.Select(st => new PipelineItem(st, active.Count(o => o.Status == st))).ToList(),
            attention,
            trend.Select(kv => new FcTrendPoint(kv.Key, kv.Value.Created, kv.Value.Shipped)).ToList(),
            channels.OrderByDescending(kv => kv.Value).Select(kv => new ChannelCount(kv.Key, kv.Value)).ToList(),
            visible.Select(w => new WarehouseLoad(w.Id, w.Name, Percent(occupied.GetValueOrDefault(w.Id), totals.GetValueOrDefault(w.Id)),
                list.Count(o => o.WarehouseId == w.Id && o.IsActive))).ToList(),
            clientOrder.Select(id => new TopClient(id, orgNames.GetValueOrDefault(id) ?? id, clients[id].Orders, clients[id].Units))
                .OrderByDescending(c => c.Orders).Take(5).ToList());
    }

    public async Task<SellerDashboardDto> SellerAsync(RequestContext ctx, string? period, CancellationToken ct)
    {
        if (!ctx.IsSeller) throw AppException.Forbidden("Дашборд доступен только селлерам");
        var sellerId = ctx.OrganizationId!;
        var now = clock.UtcNow;
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var days = PeriodDays(period);
        var periodStart = today.AddDays(-(days - 1));
        var prevStart = periodStart.AddDays(-days);

        var list = await orders.VisibleAsync(ctx, null, ct);
        var shipments = await inbound.VisibleAsync(ctx, null, ct);
        var stock = await db.Stock.AsNoTracking().Where(s => s.SellerId == sellerId).OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().Where(p => p.SellerId == sellerId).OrderBy(p => EF.Property<long>(p, "Seq")).ToListAsync(ct);
        var byId = products.ToDictionary(p => p.Id);
        var lookups = await InboundLookups.LoadAsync(db, false, ct);
        var orgs = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, ct);

        var stockUnits = stock.Sum(s => s.Quantity);
        var stockValue = stock.Sum(s => s.Quantity * (byId.GetValueOrDefault(s.ProductId)?.Price ?? 0));
        var inPeriod = list.Where(o => o.CreatedAt >= periodStart).ToList();
        var inPrev = list.Where(o => Between(o.CreatedAt, prevStart, periodStart)).ToList();

        var monthStart = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthDays = Math.Max(1, (int)Math.Ceiling((now - monthStart).TotalDays));
        var liters = stock.Sum(s => s.Quantity * (byId.GetValueOrDefault(s.ProductId)?.VolumeLiters ?? 1));
        var monthOrders = list.Where(o => o.CreatedAt >= monthStart).ToList();
        var charges = liters * 0.012 * monthDays + monthOrders.Count * 45 + monthOrders.Sum(o => o.Units) * 12;

        var sold = new Dictionary<string, int>();
        foreach (var o in list.Where(o => o.CreatedAt >= today.AddDays(-30) && o.Status != OrderStatus.Cancelled))
            foreach (var l in o.Lines) sold[l.ProductId] = sold.GetValueOrDefault(l.ProductId) + l.Quantity;
        var lowStock = products.Select(p =>
            {
                var available = stock.Where(s => s.ProductId == p.Id).Sum(s => s.Quantity - s.Reserved);
                var daily = sold.GetValueOrDefault(p.Id) / 30.0;
                return new LowStockItem(p.Id, p.Sku, p.Name, available, Math.Round(daily, 1, MidpointRounding.AwayFromZero),
                    daily > 0 ? (int)Math.Floor(available / daily) : 999);
            })
            .Where(i => i.DailySales > 0).OrderBy(i => i.DaysLeft).ToList();

        var attention = new List<AttentionItemDto>
        {
            new("low_stock", "Товары заканчиваются", "Запаса хватит менее чем на 7 дней продаж", lowStock.Count(i => i.DaysLeft < 7), "warn", "stock"),
            new("overdue", "Заказы с просроченной отгрузкой", "ФЦ не успел отгрузить в срок", list.Count(o => o.IsOverdue(now)), "danger", "orders", Q("overdue")),
            new("discrepancy", "Поставки с расхождениями", "За последние 14 дней — проверьте акты",
                shipments.Count(i => i.HasDiscrepancy && i.ExpectedAt > today.AddDays(-14)), "warn", "inbound"),
            new("in_transit", "Поставки в пути", "Ожидаются на складах ФЦ", shipments.Count(i => i.Status == InboundStatus.InTransit), "info", "inbound", Q("in_transit")),
        }.Where(a => a.Count > 0).ToList();

        var trend = Buckets(days, today).ToDictionary(k => k, _ => new int[3]);
        foreach (var o in inPeriod)
            if (trend.TryGetValue(DayKey(o.CreatedAt), out var b)) b[(int)o.Channel]++;

        var contracts = await db.Contracts.AsNoTracking().Where(c => c.SellerId == sellerId).OrderBy(c => c.Id).ToListAsync(ct);
        return new SellerDashboardDto(
            new SellerKpis(new(stockUnits), new(Math.Round(stockValue, MidpointRounding.AwayFromZero)), new(inPeriod.Count, inPrev.Count),
                new(OnTime(inPeriod), OnTime(inPrev)), new(Math.Round(charges, MidpointRounding.AwayFromZero))),
            attention,
            trend.Select(kv => new SellerTrendPoint(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2])).ToList(),
            contracts.Select(c =>
            {
                var fc = stock.Where(s => c.WarehouseIds.Contains(s.WarehouseId)).ToList();
                var org = orgs.GetValueOrDefault(c.FulfillmentId);
                return new FulfillmentStock(c.FulfillmentId, org?.Name ?? "—", org?.City ?? string.Empty, fc.Sum(s => s.Quantity),
                    fc.Where(s => s.Quantity > 0).Select(s => s.ProductId).Distinct().Count(), c.WarehouseIds.Count);
            }).ToList(),
            lowStock.Take(6).ToList(),
            shipments.Where(i => i.Status != InboundStatus.Draft).OrderByDescending(i => i.ExpectedAt).Take(5).Select(lookups.ToListItem).ToList());
    }

    private static double OnTime(List<Order> list)
    {
        var shipped = list.Where(o => o.ShippedAt is not null).ToList();
        if (shipped.Count == 0) return 100;
        return Math.Round(shipped.Count(o => o.ShippedOnTime) * 1000.0 / shipped.Count, MidpointRounding.AwayFromZero) / 10;
    }

    private static Dictionary<string, string> Q(string status) => new() { ["status"] = status };
}
