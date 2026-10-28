using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Fbo;

public record FboListItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public FboMarketplace Marketplace { get; init; }
    public string Destination { get; init; } = string.Empty;
    public FboStatus Status { get; init; }
    public string SellerId { get; init; } = string.Empty;
    public string SellerName { get; init; } = string.Empty;
    public string WarehouseId { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public DateTimeOffset PlannedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int Units { get; init; }
    public int Packed { get; init; }
    public int Boxes { get; init; }
    public int SkuCount { get; init; }
}

public sealed record FboLineDto(string ProductId, int Quantity, int Packed, string Sku, string Name, int Available);

public sealed record FboHistoryDto(DateTimeOffset At, FboStatus Status, string By);

public sealed record FboDetailsDto : FboListItemDto
{
    public string? SupplyId { get; init; }
    public IReadOnlyList<FboHistoryDto> History { get; init; } = [];
    public IReadOnlyList<FboLineDto> Lines { get; init; } = [];
}

public sealed record FboSummaryDto(int Total, IReadOnlyDictionary<string, int> ByStatus, int UnitsInWork, int DueToday);

public sealed record FboStockOptionDto(string ProductId, string Sku, string Name, int Available, string WarehouseId, string WarehouseName);

public sealed record FboQuery(PageQuery Page, FboStatus? Status, FboMarketplace? Marketplace, string? SellerId, string? WarehouseId);

public sealed record FboLineInput(string ProductId, int Quantity);

public sealed record FboInput(FboMarketplace? Marketplace, string? Destination, string? WarehouseId, DateTimeOffset? PlannedAt, IReadOnlyList<FboLineInput>? Lines);

/// <summary>FBO-отгрузки на склады маркетплейсов (mocks/handlers/fbo.ts).</summary>
public sealed class FboHandler(IAppDbContext db, IUnitOfWork uow, WarehouseScope warehouses, IIdGenerator ids, IClock clock)
{
    private const int FirstNumber = 3100;

    public async Task<FboSummaryDto> SummaryAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var list = await VisibleAsync(ctx, warehouseId, ct);
        var byStatus = new Dictionary<string, int>();
        foreach (var r in list) byStatus[SnakeCaseName.Of(r.Status)] = byStatus.GetValueOrDefault(SnakeCaseName.Of(r.Status)) + 1;
        var active = list.Where(r => FboShipment.IsActiveStatus(r.Status)).ToList();
        var end = WarehouseTime.EndOfDay(clock.UtcNow);
        return new FboSummaryDto(list.Count, byStatus, active.Sum(r => r.Units), active.Count(r => r.PlannedAt <= end));
    }

    public async Task<IReadOnlyList<FboStockOptionDto>> StockOptionsAsync(RequestContext ctx, CancellationToken ct)
    {
        if (!ctx.IsSeller) return [];
        var visible = await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        var stock = await db.Stock.AsNoTracking().Where(s => s.SellerId == ctx.OrganizationId).OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
        var l = await Lookups.LoadAsync(db, ct);
        return stock.Where(s => visible is null || visible.Contains(s.WarehouseId))
            .GroupBy(s => (s.ProductId, s.WarehouseId))
            .Select(g =>
            {
                var p = l.Products.GetValueOrDefault(g.Key.ProductId);
                return new FboStockOptionDto(g.Key.ProductId, p?.Sku ?? string.Empty, p?.Name ?? string.Empty, g.Sum(s => s.Free), g.Key.WarehouseId,
                    l.Warehouses.GetValueOrDefault(g.Key.WarehouseId) ?? string.Empty);
            })
            .Where(o => o.Available > 0)
            .OrderBy(o => o.Sku, Comparer<string>.Create(Sorting.CompareStrings))
            .ToList();
    }

    public async Task<Paged<FboListItemDto>> ListAsync(RequestContext ctx, FboQuery query, CancellationToken ct)
    {
        var l = await Lookups.LoadAsync(db, ct);
        var term = query.Page.Term;
        var items = (await VisibleAsync(ctx, query.WarehouseId, ct))
            .Where(r => (query.Status is null || r.Status == query.Status) && (query.Marketplace is null || r.Marketplace == query.Marketplace)
                        && (string.IsNullOrEmpty(query.SellerId) || r.SellerId == query.SellerId))
            .Select(r => l.Fill(new FboListItemDto(), r))
            .Where(r => term.Length == 0 || r.Number.ToLowerInvariant().Contains(term) || r.Destination.ToLowerInvariant().Contains(term));
        return Paged<FboListItemDto>.From(items.SortBy(SortKey(query.Page.SortField ?? "plannedAt"), query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }

    public async Task<FboDetailsDto> DetailsAsync(RequestContext ctx, string id, CancellationToken ct) =>
        await ToDetailsAsync(await FindAsync(ctx, id, tracking: false, ct), ct);

    public async Task<FboDetailsDto> CreateAsync(RequestContext ctx, FboInput input, CancellationToken ct)
    {
        if (ctx.OrganizationId is not { } sellerId) throw AppException.Forbidden("Недостаточно прав");
        var visible = await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        if (input.WarehouseId is not { } warehouseId || visible?.Contains(warehouseId) != true) throw AppException.Unprocessable("Выберите склад фулфилмента");
        if (input.Marketplace is not { } marketplace || input.Destination is null
            || !FboShipment.Destinations[marketplace].Contains(input.Destination)) throw AppException.Unprocessable("Выберите склад маркетплейса");
        var lines = (input.Lines ?? []).Where(x => x.Quantity > 0).ToList();
        if (lines.Count == 0) throw AppException.Unprocessable("Добавьте товары в заявку");
        var productIds = lines.Select(x => x.ProductId).ToList();
        var stock = await db.Stock.AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId) && s.WarehouseId == warehouseId && s.SellerId == sellerId).ToListAsync(ct);
        foreach (var line in lines)
        {
            var available = stock.Where(s => s.ProductId == line.ProductId).Sum(s => s.Free);
            if (line.Quantity > available)
            {
                var sku = (await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == line.ProductId, ct))?.Sku ?? line.ProductId;
                throw AppException.Unprocessable("Недостаточно товара", $"{sku}: доступно {available} шт.");
            }
        }
        var now = clock.UtcNow;
        var number = $"FBO-{FirstNumber + await db.FboShipments.CountAsync(ct) + 1}";
        var record = FboShipment.Create($"fbo_u{ids.NewSuffix()}", number, marketplace, input.Destination, sellerId, warehouseId,
            input.PlannedAt ?? now, lines.Select(x => new FboLine(x.ProductId, x.Quantity)), await UserNameAsync(ctx, ct), now);
        db.FboShipments.Add(record);
        await uow.SaveChangesAsync(ct);
        return await ToDetailsAsync(record, ct);
    }

    public Task<FboDetailsDto> SubmitAsync(RequestContext ctx, string id, CancellationToken ct) =>
        MutateAsync(ctx, id, (r, by, now) => r.Submit(Random.Shared.Next(1_000_000, 10_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture), by, now), ct);

    public Task<FboDetailsDto> CancelAsync(RequestContext ctx, string id, CancellationToken ct) =>
        MutateAsync(ctx, id, (r, by, now) => r.Cancel(by, now), ct);

    public Task<FboDetailsDto> AdvanceAsync(RequestContext ctx, string id, FboStatus? to, CancellationToken ct) =>
        MutateAsync(ctx, id, (r, by, now) =>
        {
            if (to is null) throw new DomainConflictException("Недопустимый переход статуса");
            r.Advance(to.Value, by, now);
        }, ct);

    private async Task<FboDetailsDto> MutateAsync(RequestContext ctx, string id, Action<FboShipment, string, DateTimeOffset> action, CancellationToken ct)
    {
        var record = await FindAsync(ctx, id, tracking: true, ct);
        try { action(record, await UserNameAsync(ctx, ct), clock.UtcNow); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        await uow.SaveChangesAsync(ct);
        return await ToDetailsAsync(record, ct);
    }

    private async Task<string> UserNameAsync(RequestContext ctx, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? string.Empty;

    private async Task<FboShipment> FindAsync(RequestContext ctx, string id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.FboShipments : db.FboShipments.AsNoTracking();
        var record = await query.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null || !await CanSeeAsync(ctx, record, ct)) throw AppException.NotFound("Заявка не найдена");
        return record;
    }

    private async Task<bool> CanSeeAsync(RequestContext ctx, FboShipment r, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return true;
        if (ctx.IsSeller) return r.SellerId == ctx.OrganizationId;
        return (await warehouses.VisibleWarehouseIdsAsync(ctx, ct))?.Contains(r.WarehouseId) ?? true;
    }

    private async Task<List<FboShipment>> VisibleAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var query = db.FboShipments.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(r => r.WarehouseId == warehouseId);
        if (ctx.IsSeller) query = query.Where(r => r.SellerId == ctx.OrganizationId);
        else if (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible) query = query.Where(r => visible.Contains(r.WarehouseId));
        return await query.OrderBy(r => EF.Property<long>(r, "Seq")).ToListAsync(ct);
    }

    private async Task<FboDetailsDto> ToDetailsAsync(FboShipment r, CancellationToken ct)
    {
        var l = await Lookups.LoadAsync(db, ct);
        var productIds = r.Lines.Select(x => x.ProductId).ToList();
        var stock = await db.Stock.AsNoTracking().Where(s => s.WarehouseId == r.WarehouseId && productIds.Contains(s.ProductId)).ToListAsync(ct);
        return l.Fill(new FboDetailsDto
        {
            SupplyId = r.SupplyId,
            History = r.History.Select(h => new FboHistoryDto(h.At, h.Status, h.By)).ToList(),
            Lines = r.Lines.Select(x =>
            {
                var p = l.Products.GetValueOrDefault(x.ProductId);
                return new FboLineDto(x.ProductId, x.Quantity, x.Packed, p?.Sku ?? string.Empty, p?.Name ?? string.Empty,
                    stock.Where(s => s.ProductId == x.ProductId).Sum(s => s.Free));
            }).ToList(),
        }, r);
    }

    private static Func<FboListItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => r => r.Id,
        "number" => r => r.Number,
        "marketplace" => r => SnakeCaseName.Of(r.Marketplace),
        "destination" => r => r.Destination,
        "status" => r => SnakeCaseName.Of(r.Status),
        "sellerId" => r => r.SellerId,
        "sellerName" => r => r.SellerName,
        "warehouseId" => r => r.WarehouseId,
        "warehouseName" => r => r.WarehouseName,
        "plannedAt" => r => r.PlannedAt,
        "createdAt" => r => r.CreatedAt,
        "units" => r => r.Units,
        "packed" => r => r.Packed,
        "boxes" => r => r.Boxes,
        "skuCount" => r => r.SkuCount,
        _ => null,
    };

    private sealed record Lookups(IReadOnlyDictionary<string, string> Orgs, IReadOnlyDictionary<string, string> Warehouses, IReadOnlyDictionary<string, Product> Products)
    {
        public static async Task<Lookups> LoadAsync(IAppDbContext db, CancellationToken ct) => new(
            await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct),
            await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct),
            await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct));

        public T Fill<T>(T dto, FboShipment r) where T : FboListItemDto => dto with
        {
            Id = r.Id, Number = r.Number, Marketplace = r.Marketplace, Destination = r.Destination, Status = r.Status,
            SellerId = r.SellerId, SellerName = Orgs.GetValueOrDefault(r.SellerId) ?? string.Empty,
            WarehouseId = r.WarehouseId, WarehouseName = Warehouses.GetValueOrDefault(r.WarehouseId) ?? string.Empty,
            PlannedAt = r.PlannedAt, CreatedAt = r.CreatedAt, Units = r.Units, Packed = r.Packed, Boxes = r.Boxes, SkuCount = r.Lines.Count,
        };
    }
}
