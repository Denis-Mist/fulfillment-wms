using System.Text.RegularExpressions;
using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Зона склада (приёмка, хранение, отбор…).</summary>
public partial class Zone
{
    private static readonly HashSet<ZoneType> Storable = [ZoneType.Storage, ZoneType.Picking];

    private Zone() { }

    public Zone(string id, string warehouseId, string code, string name, ZoneType type)
    {
        Id = id; WarehouseId = warehouseId; Code = code; Name = name; Type = type;
    }

    public string Id { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public ZoneType Type { get; private set; }

    /// <summary>Хранение товара допускается только в зонах хранения и отбора (FR-06).</summary>
    public bool AllowsStorage => Storable.Contains(Type);

    [GeneratedRegex("^[A-Z0-9]{2,6}$")]
    private static partial Regex CodePattern();

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static Zone Create(string warehouseId, string? code, string? name, ZoneType type)
    {
        var normalized = NormalizeCode(code);
        if (!CodePattern().IsMatch(normalized)) throw new DomainException("Код зоны: 2–6 латинских букв или цифр");
        return new Zone($"{warehouseId}_z_{normalized}", warehouseId, normalized, (name ?? string.Empty).Trim(), type);
    }
}
