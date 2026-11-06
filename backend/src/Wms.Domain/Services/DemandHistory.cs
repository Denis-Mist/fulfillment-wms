namespace Wms.Domain.Services;

/// <summary>Наблюдение спроса: заказ и/или строки отбора, привязанные к моменту времени.</summary>
public readonly record struct DemandEvent(DateTimeOffset At, int Orders, int Units, int PickLines);

/// <summary>Суточная корзина спроса (начало суток UTC).</summary>
public sealed record DailyDemand(DateTimeOffset Date, int Orders, int Units, int PickLines);

/// <summary>Сборка истории спроса из реальных заказов и строк отбора: сутки в UTC, пропуски — нули.</summary>
public static class DemandHistory
{
    public const int DefaultWindowDays = 56;
    public const double DefaultUnitsPerOrder = 2.6;

    /// <summary>
    /// Корзины за полуинтервал [today − days, today) в UTC.
    /// Последняя точка — вчера: сегодняшний день в историю не входит (как в исходном ряде прогноза).
    /// </summary>
    public static IReadOnlyList<DailyDemand> BuildUtc(DateTimeOffset today, int days, IEnumerable<DemandEvent> events)
    {
        if (days <= 0) return [];
        var start = new DateTimeOffset(today.UtcDateTime.Date, TimeSpan.Zero);
        var from = start.AddDays(-days);
        var buckets = new (int Orders, int Units, int PickLines)[days];
        foreach (var e in events)
        {
            var idx = (int)(new DateTimeOffset(e.At.UtcDateTime.Date, TimeSpan.Zero) - from).TotalDays;
            if ((uint)idx >= (uint)days) continue;
            buckets[idx].Orders += Math.Max(0, e.Orders);
            buckets[idx].Units += Math.Max(0, e.Units);
            buckets[idx].PickLines += Math.Max(0, e.PickLines);
        }

        return Enumerable.Range(0, days)
            .Select(i => new DailyDemand(from.AddDays(i), buckets[i].Orders, buckets[i].Units, buckets[i].PickLines))
            .ToList();
    }

    /// <summary>
    /// Среднее число единиц на заказ по окну.
    /// Если единиц в заказах нет, оценку дают строки отбора; иначе — <paramref name="fallback"/> (норматив мока 2.6).
    /// </summary>
    public static double AverageUnitsPerOrder(IReadOnlyList<DailyDemand> days, double fallback = DefaultUnitsPerOrder)
    {
        var orders = days.Sum(d => d.Orders);
        if (orders <= 0) return fallback;
        var units = days.Sum(d => d.Units);
        if (units > 0) return units / (double)orders;
        var lines = days.Sum(d => d.PickLines);
        return lines > 0 ? lines / (double)orders : fallback;
    }
}
