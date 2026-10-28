using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Returns;

public record ReturnListItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public SalesChannel Channel { get; init; }
    public ReturnReason Reason { get; init; }
    public ReturnStatus Status { get; init; }
    public string SellerId { get; init; } = string.Empty;
    public string SellerName { get; init; } = string.Empty;
    public string WarehouseId { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public int Units { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? InspectedAt { get; init; }
    public ReturnResolution? Resolution { get; init; }
}

public sealed record ReturnLineDto(string ProductId, string Sku, string Name, int Quantity, ReturnGrade? Grade);

public sealed record ReturnDetailsDto : ReturnListItemDto
{
    public string? Comment { get; init; }
    public string? InspectedBy { get; init; }
    public int Photos { get; init; }
    public IReadOnlyList<ReturnLineDto> Lines { get; init; } = [];
}

public sealed record ReturnSummaryDto(int Total, IReadOnlyDictionary<string, int> ByStatus, int RestockRate, int DefectUnits);

public sealed record ReturnQuery(PageQuery Page, ReturnStatus? Status, ReturnReason? Reason, string? SellerId, string? WarehouseId);

public sealed record InspectInput(IReadOnlyDictionary<string, ReturnGrade>? Grades, string? Comment);

/// <summary>Возвраты покупателей (mocks/handlers/returns.ts).</summary>
public sealed class ReturnsHandler(IAppDbContext db, IUnitOfWork uow, WarehouseScope warehouses, IClock clock)
{
    public async Task<ReturnSummaryDto> SummaryAsync(RequestContext ctx, string? warehouseId, string? sellerId, CancellationToken ct)
    {
        var list = await VisibleAsync(ctx, warehouseId, sellerId, ct);
        var byStatus = new Dictionary<string, int>();
        foreach (var r in list) byStatus[SnakeCaseName.Of(r.Status)] = byStatus.GetValueOrDefault(SnakeCaseName.Of(r.Status)) + 1;
        var graded = list.SelectMany(r => r.Lines).Where(l => l.Grade is not null).ToList();
        var restock = graded.Count(l => l.Restock);
        var rate = graded.Count == 0 ? 0 : (int)Math.Round(restock * 100.0 / graded.Count, MidpointRounding.AwayFromZero);
        return new ReturnSummaryDto(list.Count, byStatus, rate, graded.Where(l => !l.Restock).Sum(l => l.Quantity));
    }

    public async Task<Paged<ReturnListItemDto>> ListAsync(RequestContext ctx, ReturnQuery query, CancellationToken ct)
    {
        var l = await Lookups.LoadAsync(db, ct);
        var term = query.Page.Term;
        var items = (await VisibleAsync(ctx, query.WarehouseId, query.SellerId, ct))
            .Where(r => (query.Status is null || r.Status == query.Status) && (query.Reason is null || r.Reason == query.Reason))
            .Select(r => l.Fill(new ReturnListItemDto(), r))
            .Where(r => term.Length == 0 || r.Number.ToLowerInvariant().Contains(term) || r.OrderNumber.ToLowerInvariant().Contains(term));
        return Paged<ReturnListItemDto>.From(items.SortBy(SortKey(query.Page.SortField ?? "createdAt"), query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }

    public async Task<ReturnDetailsDto> DetailsAsync(RequestContext ctx, string id, CancellationToken ct) =>
        await ToDetailsAsync(await FindAsync(ctx, id, tracking: false, ct), ct);

    public Task<ReturnDetailsDto> ReceiveAsync(RequestContext ctx, string id, CancellationToken ct) =>
        MutateAsync(ctx, id, (r, _) => { r.Receive(clock.UtcNow); return Task.CompletedTask; }, ct);

    public Task<ReturnDetailsDto> InspectAsync(RequestContext ctx, string id, InspectInput input, CancellationToken ct) =>
        MutateAsync(ctx, id, async (r, token) =>
        {
            var by = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(token) ?? string.Empty;
            r.Inspect(input.Grades ?? new Dictionary<string, ReturnGrade>(), input.Comment, by, clock.UtcNow);
        }, ct);

    /// <summary>Закрытие: годный товар возвращается в первую ячейку остатка товара на складе, брак — нет (как в моке).</summary>
    public Task<ReturnDetailsDto> CloseAsync(RequestContext ctx, string id, CancellationToken ct) =>
        MutateAsync(ctx, id, async (r, token) =>
        {
            foreach (var line in r.Close())
            {
                var stock = await db.Stock.Where(s => s.ProductId == line.ProductId && s.WarehouseId == r.WarehouseId)
                    .OrderBy(s => EF.Property<long>(s, "Seq")).FirstOrDefaultAsync(token);
                stock?.Receive(line.Quantity);
            }
        }, ct);

    private async Task<ReturnDetailsDto> MutateAsync(RequestContext ctx, string id, Func<CustomerReturn, CancellationToken, Task> action, CancellationToken ct)
    {
        var record = await FindAsync(ctx, id, tracking: true, ct);
        try { await action(record, ct); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        catch (DomainValidationException e) { throw AppException.Unprocessable(e.Message, e.Detail); }
        await uow.SaveChangesAsync(ct);
        return await ToDetailsAsync(record, ct);
    }

    private async Task<CustomerReturn> FindAsync(RequestContext ctx, string id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Returns : db.Returns.AsNoTracking();
        var record = await query.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null || !await CanSeeAsync(ctx, record, ct)) throw AppException.NotFound("Возврат не найден");
        return record;
    }

    private async Task<bool> CanSeeAsync(RequestContext ctx, CustomerReturn r, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return true;
        if (ctx.IsSeller) return r.SellerId == ctx.OrganizationId;
        return (await warehouses.VisibleWarehouseIdsAsync(ctx, ct))?.Contains(r.WarehouseId) ?? true;
    }

    private async Task<List<CustomerReturn>> VisibleAsync(RequestContext ctx, string? warehouseId, string? sellerId, CancellationToken ct)
    {
        var query = db.Returns.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(r => r.WarehouseId == warehouseId);
        if (!string.IsNullOrEmpty(sellerId)) query = query.Where(r => r.SellerId == sellerId);
        if (ctx.IsSeller) query = query.Where(r => r.SellerId == ctx.OrganizationId);
        else if (await warehouses.VisibleWarehouseIdsAsync(ctx, ct) is { } visible) query = query.Where(r => visible.Contains(r.WarehouseId));
        return await query.OrderBy(r => EF.Property<long>(r, "Seq")).ToListAsync(ct);
    }

    private async Task<ReturnDetailsDto> ToDetailsAsync(CustomerReturn r, CancellationToken ct)
    {
        var l = await Lookups.LoadAsync(db, ct);
        return l.Fill(new ReturnDetailsDto
        {
            Comment = r.Comment, InspectedBy = r.InspectedBy, Photos = r.Photos,
            Lines = r.Lines.Select(x =>
            {
                var p = l.Products.GetValueOrDefault(x.ProductId);
                return new ReturnLineDto(x.ProductId, p?.Sku ?? string.Empty, p?.Name ?? string.Empty, x.Quantity, x.Grade);
            }).ToList(),
        }, r);
    }

    private static Func<ReturnListItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => r => r.Id,
        "number" => r => r.Number,
        "orderNumber" => r => r.OrderNumber,
        "channel" => r => SnakeCaseName.Of(r.Channel),
        "reason" => r => SnakeCaseName.Of(r.Reason),
        "status" => r => SnakeCaseName.Of(r.Status),
        "sellerId" => r => r.SellerId,
        "sellerName" => r => r.SellerName,
        "warehouseId" => r => r.WarehouseId,
        "warehouseName" => r => r.WarehouseName,
        "units" => r => r.Units,
        "createdAt" => r => r.CreatedAt,
        "receivedAt" => r => r.ReceivedAt,
        "inspectedAt" => r => r.InspectedAt,
        "resolution" => r => r.Resolution is { } res ? SnakeCaseName.Of(res) : null,
        _ => null,
    };

    private sealed record Lookups(IReadOnlyDictionary<string, string> Orgs, IReadOnlyDictionary<string, string> Warehouses,
        IReadOnlyDictionary<string, Product> Products, IReadOnlyDictionary<string, (string Number, SalesChannel Channel)> Orders)
    {
        public static async Task<Lookups> LoadAsync(IAppDbContext db, CancellationToken ct)
        {
            var orderIds = db.Returns.Select(r => r.OrderId);
            return new(
                await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct),
                await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, w => w.Name, ct),
                await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct),
                (await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Number, o.Channel }).ToListAsync(ct))
                    .ToDictionary(o => o.Id, o => (o.Number, o.Channel)));
        }

        public T Fill<T>(T dto, CustomerReturn r) where T : ReturnListItemDto
        {
            var order = Orders.GetValueOrDefault(r.OrderId);
            return dto with
            {
                Id = r.Id, Number = r.Number, OrderNumber = order.Number ?? "—", Channel = order.Number is null ? SalesChannel.Wb : order.Channel,
                Reason = r.Reason, Status = r.Status, SellerId = r.SellerId, SellerName = Orgs.GetValueOrDefault(r.SellerId) ?? string.Empty,
                WarehouseId = r.WarehouseId, WarehouseName = Warehouses.GetValueOrDefault(r.WarehouseId) ?? string.Empty,
                Units = r.Units, CreatedAt = r.CreatedAt, ReceivedAt = r.ReceivedAt, InspectedAt = r.InspectedAt, Resolution = r.Resolution,
            };
        }
    }
}
