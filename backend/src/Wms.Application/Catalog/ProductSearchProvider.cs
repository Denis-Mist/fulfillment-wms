using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;

namespace Wms.Application.Catalog;

/// <summary>Товары в глобальном поиске: до 5 совпадений по артикулу, названию или штрихкоду.</summary>
public sealed class ProductSearchProvider(CatalogReader reader) : ISearchProvider
{
    public const int Limit = 5;
    public int Order => 30;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(RequestContext ctx, string term, CancellationToken ct)
    {
        var products = await (await reader.VisibleProductsAsync(ctx, ct)).OrderBy(p => EF.Property<long>(p, "Seq")).ToListAsync(ct);
        var names = await reader.OrgNamesAsync(ct);
        return products
            .Where(p => p.Sku.ToLowerInvariant().Contains(term) || p.Name.ToLowerInvariant().Contains(term) || p.Barcode.Contains(term))
            .Take(Limit)
            .Select(p => new SearchResult("product", p.Id, p.Name, $"{p.Sku} · ШК {p.Barcode} · {names.GetValueOrDefault(p.SellerId)}"))
            .ToList();
    }
}
