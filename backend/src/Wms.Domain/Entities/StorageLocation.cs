using System.Text.RegularExpressions;
using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>Ячейка хранения: код «проход-стеллаж-ярус», координаты в метрах от входа, вместимость в литрах.</summary>
public partial class StorageLocation
{
    private const string AisleOrder = "ABCDEFGHJKLMNPQRSTUVWXYZ";

    private StorageLocation() { }

    public StorageLocation(string id, string warehouseId, string zoneId, string code, double x, double y, int level, double capacityLiters)
    {
        Id = id; WarehouseId = warehouseId; ZoneId = zoneId; Code = code; X = x; Y = y; Level = level; CapacityLiters = capacityLiters;
    }

    public string Id { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public string ZoneId { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public double X { get; private set; }
    public double Y { get; private set; }
    public int Level { get; private set; }
    public double CapacityLiters { get; private set; }

    /// <summary>Проход — часть кода до первого «-».</summary>
    public string Aisle => Code.Split('-')[0];

    /// <summary>Стеллаж — вторая часть кода (как Number() в моках; нечисло → 0).</summary>
    public int Rack
    {
        get
        {
            var parts = Code.Split('-');
            return parts.Length > 1 && int.TryParse(parts[1], out var rack) ? rack : 0;
        }
    }

    [GeneratedRegex("^[A-Z]{1,2}$")]
    private static partial Regex AislePattern();

    public static string CodeFor(string aisle, int rack, int level) => $"{aisle}-{rack:00}-{level}";

    public static StorageLocation Create(string warehouseId, string zoneId, string? aisle, int rack, int level, double capacityLiters)
    {
        var normalized = (aisle ?? string.Empty).Trim().ToUpperInvariant();
        if (!AislePattern().IsMatch(normalized)) throw new DomainException("Проход: 1–2 латинские буквы");
        if (rack < 1 || level < 1 || capacityLiters <= 0) throw new DomainException("Проверьте стеллаж, ярус и вместимость");
        var code = CodeFor(normalized, rack, level);
        var aisleIndex = Math.Max(0, AisleOrder.IndexOf(normalized[0]));
        return new StorageLocation($"{warehouseId}_l_{code}", warehouseId, zoneId, code,
            JsMath.Round1(4 + aisleIndex * 3.2), JsMath.Round1(3 + (rack - 1) * 1.4), level, capacityLiters);
    }
}
