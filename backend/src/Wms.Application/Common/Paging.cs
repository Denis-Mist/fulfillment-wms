namespace Wms.Application.Common;

public enum SortOrder { Asc, Desc }

/// <summary>Параметры страницы — как PageQuery во фронте.</summary>
public record PageQuery(int Page = 1, int PageSize = 20, string? Search = null, string? SortField = null, SortOrder? SortOrder = null)
{
    public const int MaxPageSize = 500;
    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize < 1 ? 20 : Math.Min(PageSize, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
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
