using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;

namespace Wms.Application.Directory;

/// <summary>Клиент ФЦ (селлер с действующим договором) для выпадающих списков.</summary>
public sealed record ClientOption(string Id, string Name, string ContractId, IReadOnlyList<string> WarehouseIds);

public sealed class ClientOptionsQueryHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<ClientOption>> HandleAsync(RequestContext ctx, CancellationToken ct)
    {
        var contracts = db.Contracts.AsNoTracking();
        if (!ctx.IsPlatformAdmin) contracts = contracts.Where(c => c.FulfillmentId == ctx.OrganizationId);
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return (await contracts.OrderBy(c => c.Id).ToListAsync(ct))
            .Select(c => new ClientOption(c.SellerId, names.GetValueOrDefault(c.SellerId) ?? "—", c.Id, c.WarehouseIds))
            .SortBy(c => c.Name, SortOrder.Asc);
    }
}
