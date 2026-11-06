using Wms.Domain.Common;

namespace Wms.Domain.Services;

/// <summary>
/// Правила слоттинга: ABC-классификация, вместимость ячейки, предпочтение нижних ярусов для тяжёлых SKU.
/// Алгоритмы «abc» и «abc_affinity» отличаются только учётом сродства (affinity); вместимость и масса — для обоих.
/// </summary>
public static class SlottingPolicy
{
    public const double AbcAShare = 0.8;
    public const double AbcBShare = 0.95;
    /// <summary>Единица тяжелее этого веса (г) предпочтительно ставится на нижние ярусы.</summary>
    public const double HeavyUnitWeightG = 1000;
    /// <summary>Минимальное сокращение пути, чтобы рекомендовать перемещение.</summary>
    public const double MinDistanceGain = 4;

    public static double Distance(double x, double y, int level) =>
        JsMath.Round1(x + y + (level - 1) * 1.5);

    public static double UsedLiters(IEnumerable<(double VolumeLiters, int Quantity)> occupants) =>
        occupants.Sum(o => Math.Max(0, o.VolumeLiters) * Math.Max(0, o.Quantity));

    public static bool Fits(double capacityLiters, double usedLiters, double addedLiters) =>
        addedLiters <= 0 || usedLiters + addedLiters <= capacityLiters + 1e-6;

    /// <summary>A — до 80% отборов, B — до 95%, иначе C. Без отборов все SKU относятся к C.</summary>
    public static IReadOnlyDictionary<string, string> ClassifyAbc(IEnumerable<(string ProductId, int Picks)> items)
    {
        var ordered = items.OrderByDescending(i => i.Picks).ToList();
        var total = ordered.Sum(i => i.Picks);
        if (ordered.Count == 0) return new Dictionary<string, string>();
        if (total <= 0) return ordered.ToDictionary(i => i.ProductId, _ => "C");
        var cls = new Dictionary<string, string>();
        var cum = 0;
        foreach (var it in ordered)
        {
            var shareBefore = cum / (double)total;
            cls[it.ProductId] = shareBefore < AbcAShare ? "A" : shareBefore < AbcBShare ? "B" : "C";
            cum += Math.Max(0, it.Picks);
        }
        return cls;
    }

    /// <summary>Меньше — лучше. Тяжёлые SKU штрафуются на высоких ярусах; при affinity близость к партнёру уменьшает оценку.</summary>
    public static double CandidateScore(double distance, int level, double unitWeightG, double affinityDistance, bool useAffinity)
    {
        var score = distance;
        if (unitWeightG >= HeavyUnitWeightG) score += Math.Max(0, level - 1) * 2.0;
        if (useAffinity) score += affinityDistance * 0.25;
        return score;
    }
}
