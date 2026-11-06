using Wms.Domain.Common;

namespace Wms.Domain.Services;

/// <summary>Заказ-кандидат в партию отбора: срок отгрузки, объём и точки маршрута.</summary>
public sealed record BatchCandidate(string Id, DateTimeOffset ShipBy, double VolumeLiters, IReadOnlyList<RouteNode> Stops);

/// <summary>Рассчитанная партия: состав, загрузка тары и оба варианта маршрута.</summary>
public sealed record BatchPlan(
    IReadOnlyList<string> OrderIds,
    double VolumeLiters,
    double ToteCapacityLiters,
    double OptimizedLength,
    double BaselineLength,
    IReadOnlyList<RouteNode> OptimizedRoute,
    IReadOnlyList<RouteNode> BaselineRoute);

/// <summary>
/// Автоформирование партий: жадное наращивание волны от самого срочного заказа
/// с учётом вместимости тары, окна отгрузки и прироста длины маршрута.
/// </summary>
public static class BatchOptimizer
{
    /// <summary>Типовой пластиковый тоут отбора, литры.</summary>
    public const double DefaultToteLiters = 60;

    public const int MaxOrders = PickListBuilder.MaxBatchOrders;

    /// <summary>Заказы с отгрузкой дальше этого окна от самого раннего в партии получают штраф к оценке.</summary>
    public static readonly TimeSpan WaveWindow = TimeSpan.FromHours(2);

    public static IReadOnlyList<BatchPlan> Plan(
        IReadOnlyList<BatchCandidate> candidates,
        double toteCapacityLiters = DefaultToteLiters)
    {
        if (candidates.Count == 0) return [];
        var remaining = candidates.OrderBy(c => c.ShipBy).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var plans = new List<BatchPlan>();
        while (remaining.Count > 0)
        {
            var batch = new List<BatchCandidate> { remaining[0] };
            remaining.RemoveAt(0);
            while (batch.Count < MaxOrders)
            {
                var earliest = batch.Min(c => c.ShipBy);
                var volume = batch.Sum(c => c.VolumeLiters);
                var currentLength = RouteLength(batch, optimized: true);
                BatchCandidate? best = null;
                var bestScore = double.PositiveInfinity;
                foreach (var candidate in remaining)
                {
                    if (volume + candidate.VolumeLiters > toteCapacityLiters + 1e-9) continue;
                    var extra = RouteLength(Concat(batch, candidate), optimized: true) - currentLength;
                    var wavePenalty = Math.Max(0, (candidate.ShipBy - earliest - WaveWindow).TotalHours) * 20;
                    var score = extra + wavePenalty;
                    if (best is null || score < bestScore - 1e-9
                        || (Math.Abs(score - bestScore) < 1e-9 && CompareTie(candidate, best) < 0))
                    {
                        best = candidate;
                        bestScore = score;
                    }
                }
                if (best is null) break;
                batch.Add(best);
                remaining.Remove(best);
            }
            plans.Add(ToPlan(batch, toteCapacityLiters));
        }
        return plans;
    }

    public static double ToteFill(double volumeLiters, double toteCapacityLiters) =>
        toteCapacityLiters <= 0 ? 0 : Math.Round(volumeLiters / toteCapacityLiters, 3, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<RouteNode> DistinctStops(IEnumerable<BatchCandidate> batch) =>
        batch.SelectMany(c => c.Stops).GroupBy(s => s.Code).Select(g => g.First()).ToList();

    private static int CompareTie(BatchCandidate a, BatchCandidate b)
    {
        var byShip = a.ShipBy.CompareTo(b.ShipBy);
        return byShip != 0 ? byShip : string.CompareOrdinal(a.Id, b.Id);
    }

    private static List<BatchCandidate> Concat(List<BatchCandidate> batch, BatchCandidate extra)
    {
        var copy = new List<BatchCandidate>(batch.Count + 1);
        copy.AddRange(batch);
        copy.Add(extra);
        return copy;
    }

    private static double RouteLength(IEnumerable<BatchCandidate> batch, bool optimized)
    {
        var stops = DistinctStops(batch);
        var ordered = optimized ? RoutePlanner.Optimized(stops, s => s) : RoutePlanner.Baseline(stops, s => s);
        return RoutePlanner.Length(ordered);
    }

    private static BatchPlan ToPlan(List<BatchCandidate> batch, double toteCapacityLiters)
    {
        var stops = DistinctStops(batch);
        var optimized = RoutePlanner.Optimized(stops, s => s);
        var baseline = RoutePlanner.Baseline(stops, s => s);
        return new BatchPlan(
            batch.Select(c => c.Id).ToList(),
            JsMath.Round1(batch.Sum(c => c.VolumeLiters)),
            toteCapacityLiters,
            RoutePlanner.Length(optimized),
            RoutePlanner.Length(baseline),
            optimized,
            baseline);
    }
}
