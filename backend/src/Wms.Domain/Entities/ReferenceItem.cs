using System.Text.RegularExpressions;
using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Запись справочника платформы (единицы, типы ячеек, перевозчики, услуги).</summary>
public partial class ReferenceItem
{
    private ReferenceItem() { }

    private ReferenceItem(string id, ReferenceKind kind, string code, string name, string? note, bool isActive)
    {
        Id = id; Kind = kind; Code = code; Name = name; Note = note; IsActive = isActive;
    }

    public string Id { get; private set; } = null!;
    public ReferenceKind Kind { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Note { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>Порядок добавления (справочник выводится в порядке создания).</summary>
    public int Position { get; private set; }

    public void PlaceAt(int position) => Position = position;

    [GeneratedRegex("^[A-Z0-9_]{2,12}$")]
    private static partial Regex CodePattern();

    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Идентификатор записи — как в моках: ref_{kind}_{CODE}.</summary>
    public static string IdFor(string kindValue, string code) => $"ref_{kindValue}_{code}";

    public static ReferenceItem Create(string id, ReferenceKind kind, string? code, string? name, string? note)
    {
        var normalized = NormalizeCode(code);
        if (!CodePattern().IsMatch(normalized)) throw new DomainException("Код: 2–12 латинских букв, цифр или «_»");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Укажите название");
        return new ReferenceItem(id, kind, normalized, name.Trim(), string.IsNullOrWhiteSpace(note) ? null : note.Trim(), true);
    }

    public static ReferenceItem Seeded(string id, ReferenceKind kind, string code, string name, string? note) =>
        new(id, kind, code, name, note, true);

    public void Toggle() => IsActive = !IsActive;
}
