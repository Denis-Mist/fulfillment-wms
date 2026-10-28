using System.Globalization;

namespace Wms.Application.Common;

public enum SortOrder { Asc, Desc }

/// <summary>Параметры страницы — как listParams() в моках: page ≥ 1, pageSize 1..200 (по умолчанию 25).</summary>
public record PageQuery(int? Page = null, int? PageSize = null, string? Search = null, string? SortField = null, SortOrder? SortOrder = null)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 200;
    public int SafePage => Page is > 0 ? Page.Value : 1;
    public int SafePageSize => PageSize switch { null or 0 => DefaultPageSize, < 1 => 1, > MaxPageSize => MaxPageSize, var v => v.Value };
    public int Skip => (SafePage - 1) * SafePageSize;
    /// <summary>Строка поиска в нижнем регистре без пробелов по краям.</summary>
    public string Term => (Search ?? string.Empty).Trim().ToLowerInvariant();
    public SortOrder OrderOr(SortOrder fallback) => SortOrder ?? fallback;
}

/// <summary>Страница результата — как Paged&lt;T&gt; во фронте.</summary>
public record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public static Paged<T> From(IEnumerable<T> source, PageQuery query)
    {
        var all = source as IReadOnlyCollection<T> ?? source.ToList();
        return new Paged<T>(all.Skip(query.Skip).Take(query.SafePageSize).ToList(), all.Count, query.SafePage, query.SafePageSize);
    }
}

/// <summary>Сортировка как sortItems() в моках: null — в конце, числа по значению, строки — по правилам ru.</summary>
public static class Sorting
{
    private static readonly CompareInfo Russian = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;

    public static int CompareStrings(string? a, string? b) => Russian.Compare(a, b, CompareOptions.None);

    public static IReadOnlyList<T> SortBy<T>(this IEnumerable<T> items, Func<T, object?>? key, SortOrder order)
    {
        var list = items.ToList();
        if (key is null) return list;
        var direction = order == SortOrder.Asc ? 1 : -1;
        var keyed = list.Select((item, index) => (item, key: key(item), index)).ToList();
        keyed.Sort((x, y) =>
        {
            var c = Compare(x.key, y.key, direction);
            return c != 0 ? c : x.index.CompareTo(y.index); // стабильность, как Array.prototype.sort
        });
        return keyed.Select(x => x.item).ToList();
    }

    private static int Compare(object? left, object? right, int direction)
    {
        if (Equals(left, right)) return 0;
        if (left is null) return 1;
        if (right is null) return -1;
        if (IsNumber(left) && IsNumber(right)) return Convert.ToDouble(left).CompareTo(Convert.ToDouble(right)) * direction;
        return CompareStrings(ToJsString(left), ToJsString(right)) * direction;
    }

    private static bool IsNumber(object v) => v is int or long or double or decimal or float;

    private static string ToJsString(object v) => v switch
    {
        bool b => b ? "true" : "false",
        DateTimeOffset d => Iso(d),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    /// <summary>ISO-строка как Date.toISOString(): UTC, миллисекунды.</summary>
    public static string Iso(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
