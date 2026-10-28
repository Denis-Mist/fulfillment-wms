using System.Globalization;
using Wms.Domain.Common;
using Wms.Domain.Entities;

namespace Wms.Domain.Services;

public sealed record PutawayCheck(string Rule, bool Passed, string Detail);

public sealed record PutawayDecision(bool Allowed, IReadOnlyList<PutawayCheck> Checks, double FreeLitersAfter);

/// <summary>Остаток в ячейке с объёмом единицы товара.</summary>
public sealed record LocationStock(string SellerId, int Quantity, double UnitVolumeLiters);

/// <summary>Правила размещения товара в ячейку (FR-06): договор, тип зоны, вместимость, масса, принадлежность.</summary>
public static class PutawayPolicy
{
    public const double HeavyItemG = 10_000;
    public const int MaxHeavyLevel = 2;

    public static double UsedLiters(IEnumerable<LocationStock> stock) =>
        JsMath.Round1(stock.Sum(s => s.Quantity * s.UnitVolumeLiters));

    public static PutawayDecision Evaluate(StorageLocation location, Zone? zone, Product product, double quantity,
        IReadOnlyCollection<LocationStock> stock, bool contracted)
    {
        var free = location.CapacityLiters - UsedLiters(stock);
        var needed = product.VolumeLiters * quantity;
        var foreign = stock.Any(s => s.SellerId != product.SellerId);
        var storable = zone?.AllowsStorage ?? false;
        var light = product.WeightG < HeavyItemG;
        var kg = Fixed1(product.WeightG / 1000);
        var checks = new List<PutawayCheck>
        {
            new("Договор хранения", contracted, contracted ? "Клиент обслуживается на этом складе" : "У клиента нет договора на этот склад"),
            new("Тип зоны", storable, storable ? $"{zone!.Name} допускает хранение" : $"{zone?.Name ?? "Зона"} не предназначена для хранения"),
            new("Вместимость", needed <= free,
                $"Нужно {Fixed1(needed)} л, свободно {Fixed1(Math.Max(0, free))} л из {Js(location.CapacityLiters)} л"),
            new("Габариты и масса", light || location.Level <= MaxHeavyLevel,
                light ? $"Масса единицы {kg} кг" : $"Тяжёлый товар ({kg} кг) — только ярусы 1–{MaxHeavyLevel}"),
            new("Принадлежность ячейки", !foreign, foreign ? "В ячейке хранится товар другого клиента" : "В ячейке нет товара других клиентов"),
        };
        return new PutawayDecision(checks.All(c => c.Passed), checks, JsMath.Round1(free - needed));
    }

    private static string Fixed1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
    private static string Js(double v) => v.ToString(CultureInfo.InvariantCulture);
}
