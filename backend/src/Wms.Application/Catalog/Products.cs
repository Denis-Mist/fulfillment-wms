using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;

namespace Wms.Application.Catalog;

public sealed record ProductListItem(string Id, string Sku, string Name, string Category, string Barcode, string? WbBarcode,
    string SellerId, string SellerName, double Price, double WeightG, double VolumeLiters, bool TracksExpiry, bool RequiresMarking,
    int StockQty, bool Archived);

public sealed record ProductWarehouseStock(string WarehouseId, string WarehouseName, int Quantity, int Reserved, int Locations);

public sealed record ProductDetails(string Id, string Sku, string Name, string Category, string Barcode, string? WbBarcode,
    string SellerId, string SellerName, double Price, double WeightG, double VolumeLiters, bool TracksExpiry, bool RequiresMarking,
    int StockQty, bool Archived, IReadOnlyList<ProductWarehouseStock> Stock, int Sold30d);

public sealed record CategoryFacet(string Value, int Count);

public sealed record ProductFacets(int Total, IReadOnlyList<CategoryFacet> Categories, int Marking, int Expiry, int NoStock, int Archived);

public sealed record ImportError(int Row, string Message);

public sealed record ImportResult(int Created, int Updated, IReadOnlyList<ImportError> Errors);

public sealed record ImportInput(IReadOnlyList<ProductDraft>? Rows);

public sealed record ProductQuery(PageQuery Page, string? SellerId, string? Category, string? Flag);

/// <summary>Порт продаж по товару за 30 дней.</summary>
public interface IProductSalesSource
{
    Task<int> Sold30dAsync(string productId, IReadOnlyCollection<string>? warehouseIds, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Видимые товары и их остатки на видимых складах — общая основа каталога.</summary>
public sealed class CatalogReader(IAppDbContext db, WarehouseScope scope)
{
    public async Task<IQueryable<Product>> VisibleProductsAsync(RequestContext ctx, CancellationToken ct)
    {
        var sellers = await scope.VisibleSellerIdsAsync(ctx, ct);
        var products = db.Products.AsNoTracking();
        return sellers is null ? products : products.Where(p => sellers.Contains(p.SellerId));
    }

    public async Task<Dictionary<string, int>> StockByProductAsync(RequestContext ctx, CancellationToken ct)
    {
        var warehouses = await scope.VisibleWarehouseIdsAsync(ctx, ct);
        var stock = db.Stock.AsNoTracking();
        if (warehouses is not null) stock = stock.Where(s => warehouses.Contains(s.WarehouseId));
        return await stock.GroupBy(s => s.ProductId).Select(g => new { g.Key, Qty = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
    }

    public Task<Dictionary<string, string>> OrgNamesAsync(CancellationToken ct) =>
        db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);

    public static ProductListItem ToItem(Product p, IReadOnlyDictionary<string, int> stock, IReadOnlyDictionary<string, string> names) =>
        new(p.Id, p.Sku, p.Name, p.Category, p.Barcode, p.WbBarcode, p.SellerId, names.GetValueOrDefault(p.SellerId) ?? "—", p.Price,
            p.WeightG, p.VolumeLiters, p.TracksExpiry, p.RequiresMarking, stock.GetValueOrDefault(p.Id), p.Archived);

    /// <summary>filtered() из моков: селлер, категория (только для списка), поиск, флаг.</summary>
    public async Task<IReadOnlyList<ProductListItem>> FilteredAsync(RequestContext ctx, ProductQuery query, bool withFlag, CancellationToken ct)
    {
        var products = await (await VisibleProductsAsync(ctx, ct)).OrderBy(p => EF.Property<long>(p, "Seq")).ToListAsync(ct);
        var stock = await StockByProductAsync(ctx, ct);
        var names = await OrgNamesAsync(ct);
        var term = query.Page.Term;
        var flag = withFlag ? query.Flag : null;
        return products
            .Where(p => string.IsNullOrEmpty(query.SellerId) || p.SellerId == query.SellerId)
            .Where(p => !withFlag || string.IsNullOrEmpty(query.Category) || p.Category == query.Category)
            .Select(p => ToItem(p, stock, names))
            .Where(p => term.Length == 0 || new[] { p.Sku, p.Name, p.Barcode, p.WbBarcode ?? "", p.SellerName }
                .Any(v => v.ToLowerInvariant().Contains(term)))
            .Where(p => flag switch
            {
                "archived" => p.Archived,
                _ when !withFlag => true,
                _ when p.Archived => false,
                "marking" => p.RequiresMarking,
                "expiry" => p.TracksExpiry,
                "no_stock" => p.StockQty == 0,
                _ => true,
            })
            .ToList();
    }
}

public sealed class ProductListHandler(CatalogReader reader)
{
    public async Task<Paged<ProductListItem>> HandleAsync(RequestContext ctx, ProductQuery query, CancellationToken ct)
    {
        var items = await reader.FilteredAsync(ctx, query, withFlag: true, ct);
        return Paged<ProductListItem>.From(items.SortBy(SortKey(query.Page.SortField ?? "name"), query.Page.OrderOr(SortOrder.Asc)), query.Page);
    }

    private static Func<ProductListItem, object?>? SortKey(string field) => field switch
    {
        "id" => p => p.Id,
        "sku" => p => p.Sku,
        "name" => p => p.Name,
        "category" => p => p.Category,
        "barcode" => p => p.Barcode,
        "wbBarcode" => p => p.WbBarcode,
        "sellerId" => p => p.SellerId,
        "sellerName" => p => p.SellerName,
        "price" => p => p.Price,
        "weightG" => p => p.WeightG,
        "volumeLiters" => p => p.VolumeLiters,
        "tracksExpiry" => p => p.TracksExpiry,
        "requiresMarking" => p => p.RequiresMarking,
        "stockQty" => p => p.StockQty,
        "archived" => p => p.Archived,
        _ => null,
    };
}

public sealed class ProductFacetsHandler(CatalogReader reader)
{
    public async Task<ProductFacets> HandleAsync(RequestContext ctx, ProductQuery query, CancellationToken ct)
    {
        var items = await reader.FilteredAsync(ctx, query, withFlag: false, ct);
        var active = items.Where(p => !p.Archived).ToList();
        var categories = active.GroupBy(p => p.Category).Select(g => new CategoryFacet(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ToList(); // стабильная сортировка, как Array.prototype.sort
        return new ProductFacets(active.Count, categories, active.Count(p => p.RequiresMarking), active.Count(p => p.TracksExpiry),
            active.Count(p => p.StockQty == 0), items.Count - active.Count);
    }
}

public sealed class ProductDetailsHandler(IAppDbContext db, CatalogReader reader, WarehouseScope scope, IProductSalesSource sales, IClock clock)
{
    public async Task<ProductDetails> HandleAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var product = await (await reader.VisibleProductsAsync(ctx, ct)).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw AppException.NotFound("Товар не найден");
        var warehouses = await scope.VisibleWarehouseIdsAsync(ctx, ct);
        var names = await reader.OrgNamesAsync(ct);
        var warehouseNames = await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var rows = (await db.Stock.AsNoTracking().Where(s => s.ProductId == product.Id).OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct))
            .Where(s => warehouses is null || warehouses.Contains(s.WarehouseId)).ToList();
        var byWarehouse = rows.GroupBy(s => s.WarehouseId)
            .Select(g => new ProductWarehouseStock(g.Key, warehouseNames.GetValueOrDefault(g.Key) ?? "—", g.Sum(s => s.Quantity),
                g.Sum(s => s.Reserved), g.Count()))
            .ToList();
        var item = CatalogReader.ToItem(product, await reader.StockByProductAsync(ctx, ct), names);
        return new ProductDetails(item.Id, item.Sku, item.Name, item.Category, item.Barcode, item.WbBarcode, item.SellerId, item.SellerName,
            item.Price, item.WeightG, item.VolumeLiters, item.TracksExpiry, item.RequiresMarking, item.StockQty, item.Archived,
            byWarehouse, await sales.Sold30dAsync(product.Id, warehouses, clock.UtcNow, ct));
    }
}

/// <summary>Создание, изменение, импорт и архивирование карточек (только роли селлера).</summary>
public sealed class ProductCommandsHandler(IAppDbContext db, CatalogReader reader, IIdGenerator ids)
{
    public async Task<ProductListItem> CreateAsync(RequestContext ctx, ProductDraft? input, CancellationToken ct)
    {
        var draft = input ?? Empty;
        var sellerId = ctx.OrganizationId!;
        if (await ValidateAsync(draft, sellerId, null, ct) is { } error) throw AppException.Unprocessable(error);
        var product = Product.Create(NewId(), sellerId, draft);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(ctx, product, ct);
    }

    public async Task<ProductListItem> UpdateAsync(RequestContext ctx, string id, ProductDraft? input, CancellationToken ct)
    {
        var product = await FindVisibleAsync(ctx, id, ct);
        var draft = input ?? Empty;
        if (await ValidateAsync(draft, product.SellerId, product.Id, ct) is { } error) throw AppException.Unprocessable(error);
        product.Update(draft);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(ctx, product, ct);
    }

    public async Task<ProductListItem> ActAsync(RequestContext ctx, string id, string action, CancellationToken ct)
    {
        var product = await FindVisibleAsync(ctx, id, ct);
        var stock = await reader.StockByProductAsync(ctx, ct);
        switch (action)
        {
            case "archive":
                try { product.Archive(stock.GetValueOrDefault(product.Id)); }
                catch (DomainConflictException e) { throw AppException.Conflict(e.Message, "Сначала отгрузите или спишите остаток на складах."); }
                break;
            case "restore":
                product.Restore();
                break;
            default:
                throw AppException.NotFound("Неизвестное действие");
        }
        await db.SaveChangesAsync(ct);
        return CatalogReader.ToItem(product, stock, await reader.OrgNamesAsync(ct));
    }

    /// <summary>Импорт: строка — создание или обновление по артикулу; ошибки с номером строки файла (заголовок — строка 1).</summary>
    public async Task<ImportResult> ImportAsync(RequestContext ctx, ImportInput? input, CancellationToken ct)
    {
        var sellerId = ctx.OrganizationId!;
        int created = 0, updated = 0;
        var errors = new List<ImportError>();
        var rows = input?.Rows ?? [];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index] ?? Empty;
            var sku = row.NormalizedSku.ToLowerInvariant();
            var existing = (await db.Products.Where(p => p.SellerId == sellerId).OrderBy(p => EF.Property<long>(p, "Seq")).ToListAsync(ct))
                .FirstOrDefault(p => p.Sku.ToLowerInvariant() == sku);
            if (await ValidateAsync(row, sellerId, existing?.Id, ct) is { } error)
            {
                errors.Add(new ImportError(index + 2, error));
                continue;
            }
            if (existing is not null) { existing.Update(row); updated++; }
            else { db.Products.Add(Product.Create(NewId(), sellerId, row)); created++; }
            await db.SaveChangesAsync(ct);
        }
        return new ImportResult(created, updated, errors);
    }

    private static readonly ProductDraft Empty = new(null, null, null, null, null, null, null, null, null, null);

    private string NewId() => $"prd_{ids.NewSuffix()}";

    private async Task<Product> FindVisibleAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var visible = await (await reader.VisibleProductsAsync(ctx, ct)).AnyAsync(p => p.Id == id, ct);
        return (visible ? await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) : null) ?? throw AppException.NotFound("Товар не найден");
    }

    private async Task<string?> ValidateAsync(ProductDraft draft, string sellerId, string? selfId, CancellationToken ct)
    {
        if (Product.Validate(draft) is { } error) return error;
        var duplicate = (await db.Products.Where(p => p.SellerId == sellerId && p.Id != selfId)
                .OrderBy(p => EF.Property<long>(p, "Seq")).ToListAsync(ct))
            .FirstOrDefault(p => p.ConflictsWith(draft));
        return duplicate is null ? null : $"Товар с таким артикулом или штрихкодом уже есть: {duplicate.Sku}";
    }

    private async Task<ProductListItem> ItemAsync(RequestContext ctx, Product product, CancellationToken ct) =>
        CatalogReader.ToItem(product, await reader.StockByProductAsync(ctx, ct), await reader.OrgNamesAsync(ct));
}
