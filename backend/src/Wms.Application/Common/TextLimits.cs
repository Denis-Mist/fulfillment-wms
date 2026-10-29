namespace Wms.Application.Common;

/// <summary>
/// Политика длины строковых полей входящих запросов: короткие поля (названия, email, адреса, коды) — до 256 символов,
/// свободный текст и ключи API — до 2000. Превышение → 422 с ошибками по полям вместо 500 от БД.
/// </summary>
public static class TextLimits
{
    public const int Default = 256;
    public const int LongText = 2000;
    public const string TooLongTitle = "Слишком длинное значение";

    private static readonly HashSet<string> LongFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "comment", "note", "notes", "message", "reason", "description", "apiKey", "token",
    };

    /// <summary>Максимальная длина для поля по его имени (последнему сегменту пути).</summary>
    public static int MaxFor(string fieldName) => LongFields.Contains(fieldName) ? LongText : Default;

    public static string TooLong(int max) => $"Не более {max} символов";

    /// <summary>Добавляет ошибку, если значение длиннее лимита.</summary>
    public static void Check(IDictionary<string, string[]> errors, string path, string? value, int? max = null)
    {
        var limit = max ?? MaxFor(path[(path.LastIndexOf('.') + 1)..]);
        if (value is not null && value.Length > limit && !errors.ContainsKey(path)) errors[path] = [TooLong(limit)];
    }
}
