using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Inbound;

public record InboundListItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public InboundStatus Status { get; init; }
    public string SellerId { get; init; } = string.Empty;
    public string SellerName { get; init; } = string.Empty;
    public string FulfillmentName { get; init; } = string.Empty;
    public string WarehouseId { get; init; } = string.Empty;
    public string WarehouseName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpectedAt { get; init; }
    public PackageType PackageType { get; init; }
    public int PackagesCount { get; init; }
    public int LinesCount { get; init; }
    public int ExpectedUnits { get; init; }
    public int ReceivedUnits { get; init; }
    public int DefectUnits { get; init; }
    public bool HasDiscrepancy { get; init; }
}

public sealed record InboundLineDto(string Id, string ProductId, string Sku, string Name, string Barcode, int ExpectedQty,
    int ReceivedQty, int DefectQty, bool TracksExpiry, string? LotNumber, DateTimeOffset? ExpiresAt);

public sealed record InboundEventDto(string Id, DateTimeOffset At, InboundEventType Type, string Title, string? Description, string ActorName);

public sealed record InboundShipmentDto : InboundListItemDto
{
    public IReadOnlyList<InboundLineDto> Lines { get; init; } = [];
    public IReadOnlyList<InboundEventDto> Events { get; init; } = [];
    public string? Comment { get; init; }
    public string? DriverName { get; init; }
    public string? VehicleNumber { get; init; }
}

public sealed record InboundSummary(int Total, IReadOnlyDictionary<string, int> ByStatus);

public sealed record InboundQuery(PageQuery Page, InboundStatus? Status, string? SellerId, string? WarehouseId);

/// <summary>Справочники для отображения поставок (как lookups() в моках).</summary>
internal sealed record InboundLookups(IReadOnlyDictionary<string, string> OrgNames, IReadOnlyDictionary<string, Warehouse> Warehouses,
    IReadOnlyDictionary<string, Product> Products)
{
    public static async Task<InboundLookups> LoadAsync(IAppDbContext db, bool withProducts, CancellationToken ct) => new(
        await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct),
        await db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.Id, ct),
        withProducts ? await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct) : new Dictionary<string, Product>());

    public InboundListItemDto ToListItem(InboundShipment s) => Fill(new InboundListItemDto(), s);

    public InboundShipmentDto ToDetails(InboundShipment s) => Fill(new InboundShipmentDto
    {
        Comment = s.Comment, DriverName = s.DriverName, VehicleNumber = s.VehicleNumber,
        Events = s.Events.Select(e => new InboundEventDto(e.Id, e.At, e.Type, e.Title, e.Description, e.ActorName)).ToList(),
        Lines = s.Lines.Select(l =>
        {
            var p = Products.GetValueOrDefault(l.ProductId);
            return new InboundLineDto(l.Id, l.ProductId, p?.Sku ?? "—", p?.Name ?? "Товар удалён", p?.Barcode ?? string.Empty,
                l.ExpectedQty, l.ReceivedQty, l.DefectQty, p?.TracksExpiry ?? false, l.LotNumber, l.ExpiresAt);
        }).ToList(),
    }, s);

    private T Fill<T>(T dto, InboundShipment s) where T : InboundListItemDto
    {
        var warehouse = Warehouses.GetValueOrDefault(s.WarehouseId);
        return dto with
        {
            Id = s.Id, Number = s.Number, Status = s.Status, SellerId = s.SellerId,
            SellerName = OrgNames.GetValueOrDefault(s.SellerId) ?? "—",
            FulfillmentName = (warehouse is null ? null : OrgNames.GetValueOrDefault(warehouse.OrganizationId)) ?? "—",
            WarehouseId = s.WarehouseId, WarehouseName = warehouse?.Name ?? "—",
            CreatedAt = s.CreatedAt, ExpectedAt = s.ExpectedAt, PackageType = s.PackageType, PackagesCount = s.PackagesCount,
            LinesCount = s.Lines.Count, ExpectedUnits = s.ExpectedUnits, ReceivedUnits = s.ReceivedUnits, DefectUnits = s.DefectUnits,
            HasDiscrepancy = s.HasDiscrepancy,
        };
    }
}

/// <summary>Видимость поставок: селлер — свои, ФЦ — на своих складах, администратор — все (canSee/scoped в моках).</summary>
public sealed class InboundScope(IAppDbContext db, WarehouseScope warehouses)
{
    public async Task<IReadOnlyList<InboundShipment>> VisibleAsync(RequestContext ctx, string? warehouseId, CancellationToken ct)
    {
        var visible = ctx.IsSeller ? null : await warehouses.VisibleWarehouseIdsAsync(ctx, ct);
        var query = db.InboundShipments.AsNoTracking();
        if (!string.IsNullOrEmpty(warehouseId)) query = query.Where(s => s.WarehouseId == warehouseId);
        if (ctx.IsSeller) query = query.Where(s => s.SellerId == ctx.OrganizationId);
        else if (visible is not null) query = query.Where(s => visible.Contains(s.WarehouseId));
        return await query.OrderBy(s => EF.Property<long>(s, "Seq")).ToListAsync(ct);
    }

    public async Task<bool> CanSeeAsync(RequestContext ctx, InboundShipment s, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return true;
        if (ctx.IsSeller) return s.SellerId == ctx.OrganizationId;
        return (await warehouses.VisibleWarehouseIdsAsync(ctx, ct))?.Contains(s.WarehouseId) ?? true;
    }
}

public sealed class InboundQueryHandler(IAppDbContext db, InboundScope scope)
{
    public async Task<Paged<InboundListItemDto>> ListAsync(RequestContext ctx, InboundQuery query, CancellationToken ct)
    {
        var items = await FilteredAsync(ctx, query, withStatus: true, ct);
        return Paged<InboundListItemDto>.From(items.SortBy(SortKey(query.Page.SortField ?? "expectedAt"), query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }

    public async Task<InboundSummary> SummaryAsync(RequestContext ctx, InboundQuery query, CancellationToken ct)
    {
        var items = await FilteredAsync(ctx, query, withStatus: false, ct);
        var byStatus = new Dictionary<string, int>();
        foreach (var item in items)
        {
            var key = SnakeCaseName.Of(item.Status);
            byStatus[key] = byStatus.GetValueOrDefault(key) + 1;
        }
        return new InboundSummary(items.Count, byStatus);
    }

    public async Task<InboundShipmentDto> DetailsAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var shipment = await db.InboundShipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null || !await scope.CanSeeAsync(ctx, shipment, ct)) throw AppException.NotFound("Поставка не найдена");
        return (await InboundLookups.LoadAsync(db, withProducts: true, ct)).ToDetails(shipment);
    }

    private async Task<IReadOnlyList<InboundListItemDto>> FilteredAsync(RequestContext ctx, InboundQuery query, bool withStatus, CancellationToken ct)
    {
        var lookups = await InboundLookups.LoadAsync(db, withProducts: false, ct);
        var term = query.Page.Term;
        return (await scope.VisibleAsync(ctx, query.WarehouseId, ct))
            .Where(s => (!withStatus || query.Status is null || s.Status == query.Status) &&
                        (string.IsNullOrEmpty(query.SellerId) || s.SellerId == query.SellerId))
            .Select(lookups.ToListItem)
            .Where(i => term.Length == 0 || i.Number.ToLowerInvariant().Contains(term) ||
                        i.SellerName.ToLowerInvariant().Contains(term) || i.WarehouseName.ToLowerInvariant().Contains(term))
            .ToList();
    }

    private static Func<InboundListItemDto, object?>? SortKey(string field) => field switch
    {
        "id" => i => i.Id,
        "number" => i => i.Number,
        "status" => i => SnakeCaseName.Of(i.Status),
        "sellerId" => i => i.SellerId,
        "sellerName" => i => i.SellerName,
        "fulfillmentName" => i => i.FulfillmentName,
        "warehouseId" => i => i.WarehouseId,
        "warehouseName" => i => i.WarehouseName,
        "createdAt" => i => i.CreatedAt,
        "expectedAt" => i => i.ExpectedAt,
        "packageType" => i => SnakeCaseName.Of(i.PackageType),
        "packagesCount" => i => i.PackagesCount,
        "linesCount" => i => i.LinesCount,
        "expectedUnits" => i => i.ExpectedUnits,
        "receivedUnits" => i => i.ReceivedUnits,
        "defectUnits" => i => i.DefectUnits,
        "hasDiscrepancy" => i => i.HasDiscrepancy,
        _ => null,
    };
}
