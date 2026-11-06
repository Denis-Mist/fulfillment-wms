namespace Wms.Domain.Services;

/// <summary>
/// Показатели KPI (FR-17): сравнение маршрутов baseline/optimized, загрузка персонала и доля ошибок.
/// </summary>
public static class KpiMetrics
{
    public const int ShiftHours = 12;

    public static DateTimeOffset UtcDay(DateTimeOffset at) => new(at.UtcDateTime.Date, TimeSpan.Zero);

    /// <summary>Длины замкнутого маршрута по кодам ячеек и оптимизированного (ближайший сосед + 2-opt).</summary>
    public static (double Baseline, double Optimized) RoutePair(IEnumerable<RouteNode> stops)
    {
        var list = stops.ToList();
        if (list.Count == 0) return (0, 0);
        var baseline = RoutePlanner.Length(RoutePlanner.Baseline(list, n => n));
        var optimized = RoutePlanner.Length(RoutePlanner.Optimized(list, n => n));
        return (baseline, optimized);
    }

    public static double ReductionPct(double baseline, double optimized) =>
        baseline <= 0 ? 0 : Math.Floor((1 - optimized / baseline) * 1000 + 0.5) / 10.0;

    public static int Utilization(double workMinutes, int scheduledStaff, int shiftHours = ShiftHours)
    {
        var available = scheduledStaff * shiftHours * 60.0;
        if (available <= 0) return 0;
        return Math.Clamp((int)Math.Floor(100.0 * workMinutes / available + 0.5), 0, 100);
    }

    public static double ErrorRate(int errors, int orders) =>
        orders <= 0 ? 0 : Math.Floor(errors / (double)orders * 1000 + 0.5) / 10.0;

    public static double AverageOrZero(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? 0 : values.Average();
}
