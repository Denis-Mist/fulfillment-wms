using System.Globalization;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Billing;

public sealed record Charge(string Id, string ContractId, string Date, BillingService Service, decimal Quantity, string Unit, decimal Rate, decimal Amount);

/// <summary>Операции склада селлера на складах договора — вход для расчёта начислений.</summary>
public sealed record ContractActivity(IReadOnlyList<Order> Orders, IReadOnlyList<InboundShipment> Inbound, double StockLiters);

/// <summary>
/// Начисления по договору (buildCharges() из billing.ts): из заказов, приёмок и объёма хранения
/// плюс детерминированная синтетика базового потока, FBO и возвратов. Чистая функция.
/// </summary>
public static class ChargeCalculator
{
    public const int HistoryDays = 120;

    public static readonly IReadOnlyDictionary<BillingService, string> Units = new Dictionary<BillingService, string>
    {
        [BillingService.Receiving] = "шт", [BillingService.Storage] = "м³·сут", [BillingService.Picking] = "заказ",
        [BillingService.Packing] = "шт", [BillingService.Fbo] = "короб", [BillingService.Returns] = "шт",
    };

    private static readonly OrderStatus[] Billable = [OrderStatus.Picked, OrderStatus.Packed, OrderStatus.Shipped];

    public static string DayKey(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string MonthKey(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static IReadOnlyList<Charge> Build(Contract c, Tariff t, ContractActivity activity, DateTimeOffset now)
    {
        var wh = c.WarehouseIds.ToHashSet();
        var start = c.StartedAt > now.AddDays(-HistoryDays) ? c.StartedAt : now.AddDays(-HistoryDays);
        var byDay = new SortedDictionary<string, Dictionary<BillingService, decimal>>(StringComparer.Ordinal);
        void Add(DateTimeOffset at, BillingService s, decimal q)
        {
            var key = DayKey(at);
            if (!byDay.TryGetValue(key, out var rec)) byDay[key] = rec = [];
            rec[s] = rec.GetValueOrDefault(s) + q;
        }

        foreach (var o in activity.Orders)
        {
            if (o.SellerId != c.SellerId || !wh.Contains(o.WarehouseId) || !Billable.Contains(o.Status) || o.CreatedAt < start) continue;
            var units = o.Lines.Sum(l => l.Quantity);
            Add(o.CreatedAt, BillingService.Picking, 1);
            Add(o.CreatedAt, BillingService.Packing, units);
            var rng = new SeededRandom($"{c.Id}:{o.Id}");
            if (o.Status == OrderStatus.Shipped && rng.Bool(0.08))
            {
                var at = o.CreatedAt.AddDays(rng.Int(3, 10));
                Add(at < now ? at : now, BillingService.Returns, units);
            }
        }
        foreach (var i in activity.Inbound)
        {
            if (i.SellerId != c.SellerId || !wh.Contains(i.WarehouseId) || i.ExpectedAt < start) continue;
            var units = i.Lines.Sum(l => l.ReceivedQty);
            if (units > 0) Add(i.ExpectedAt < now ? i.ExpectedAt : now, BillingService.Receiving, units);
        }
        for (var d = start; d <= now; d = d.AddDays(1))
        {
            var rng = new SeededRandom($"{c.Id}:{DayKey(d)}");
            Add(d, BillingService.Storage, (decimal)Math.Round(activity.StockLiters / 1000 * (0.85 + rng.Float(0, 0.3)) * 10, MidpointRounding.AwayFromZero) / 10);
            if (d.UtcDateTime.DayOfWeek == DayOfWeek.Tuesday || rng.Bool(0.1)) Add(d, BillingService.Fbo, rng.Int(4, 24));
            var extra = rng.Int(2, 14);
            Add(d, BillingService.Picking, extra);
            Add(d, BillingService.Packing, extra * rng.Int(1, 3));
            if (rng.Bool(0.25)) Add(d, BillingService.Receiving, rng.Int(40, 400));
        }

        var result = new List<Charge>();
        foreach (var (date, rec) in byDay)
        foreach (var s in Enum.GetValues<BillingService>())
        {
            if (!rec.TryGetValue(s, out var q)) continue;
            var rate = t.RateFor(s);
            result.Add(new Charge($"{c.Id}:{date}:{SnakeCaseName.Of(s)}", c.Id, date, s, q, Units[s], rate,
                Math.Round(q * rate, 2, MidpointRounding.AwayFromZero)));
        }
        return result;
    }
}
