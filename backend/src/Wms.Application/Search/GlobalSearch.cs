using Wms.Application.Abstractions;
using Wms.Application.Auth;

namespace Wms.Application.Search;

/// <summary>Глобальный поиск: запрос от 2 символов, результаты провайдеров в порядке заказы → поставки → товары.</summary>
public sealed class GlobalSearchHandler(IEnumerable<ISearchProvider> providers)
{
    public const int MinLength = 2;

    public async Task<IReadOnlyList<SearchResult>> HandleAsync(RequestContext ctx, string? q, CancellationToken ct)
    {
        var term = (q ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length < MinLength) return [];
        var results = new List<SearchResult>();
        foreach (var provider in providers.OrderBy(p => p.Order)) results.AddRange(await provider.SearchAsync(ctx, term, ct));
        return results;
    }
}
