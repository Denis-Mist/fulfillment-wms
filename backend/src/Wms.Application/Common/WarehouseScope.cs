using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;

namespace Wms.Application.Common;

/// <summary>Видимость складов и товаров для организации — как lib/scope.ts и visibleProducts() в моках.</summary>
public sealed class WarehouseScope(IAppDbContext db)
{
    /// <summary>Склады организации; null — все (администратор платформы).</summary>
    public async Task<HashSet<string>?> VisibleWarehouseIdsAsync(RequestContext ctx, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return null;
        if (ctx.IsFulfillment)
            return (await db.Warehouses.AsNoTracking().Where(w => w.OrganizationId == ctx.OrganizationId).Select(w => w.Id).ToListAsync(ct)).ToHashSet();
        var lists = await db.Contracts.AsNoTracking().Where(c => c.SellerId == ctx.OrganizationId).Select(c => c.WarehouseIds).ToListAsync(ct);
        return lists.SelectMany(l => l).ToHashSet();
    }

    /// <summary>Селлеры, чьи товары видит организация; null — все.</summary>
    public async Task<HashSet<string>?> VisibleSellerIdsAsync(RequestContext ctx, CancellationToken ct)
    {
        if (ctx.IsPlatformAdmin) return null;
        if (ctx.IsSeller) return [ctx.OrganizationId!];
        return (await db.Contracts.AsNoTracking().Where(c => c.FulfillmentId == ctx.OrganizationId).Select(c => c.SellerId).ToListAsync(ct)).ToHashSet();
    }

    /// <summary>Склад, если он виден организации; иначе 404 «Склад не найден».</summary>
    public async Task<Domain.Entities.Warehouse> RequireWarehouseAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var visible = await VisibleWarehouseIdsAsync(ctx, ct);
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse is null || (visible is not null && !visible.Contains(id))) throw AppException.NotFound("Склад не найден");
        return warehouse;
    }
}
