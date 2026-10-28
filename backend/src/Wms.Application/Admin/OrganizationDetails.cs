using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Admin;

public sealed record OrganizationUserDto(string Id, string FullName, string Email, Role Role);
public sealed record OrganizationWarehouseDto(string Id, string Name, string Code, string City);
public sealed record OrganizationPartnerDto(string Id, string Name, DateTimeOffset StartedAt, int Warehouses);

public sealed record OrganizationDetailsDto(string Id, string Name, OrganizationType Type, string Inn, string City,
    OrganizationStatus Status, DateTimeOffset CreatedAt, IReadOnlyList<OrganizationUserDto> Users,
    IReadOnlyList<OrganizationWarehouseDto> Warehouses, IReadOnlyList<OrganizationPartnerDto> Partners, int Orders30d, int Products);

public sealed class OrganizationDetailsHandler(IAppDbContext db, IOperationalMetrics metrics, IClock clock)
{
    public async Task<OrganizationDetailsDto> HandleAsync(string id, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw AppException.NotFound("Организация не найдена");
        var isFc = org.Type == OrganizationType.Fulfillment;
        var warehouses = await db.Warehouses.AsNoTracking().Where(w => w.OrganizationId == id).OrderBy(w => w.Id).ToListAsync(ct);
        var contracts = await db.Contracts.AsNoTracking().Where(c => (isFc ? c.FulfillmentId : c.SellerId) == id).OrderBy(c => c.Id).ToListAsync(ct);
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var users = (await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct))
            .Where(u => u.Memberships.Any(m => m.OrganizationId == id))
            .Select(u => new OrganizationUserDto(u.Id, u.FullName, u.Email, u.Memberships.First(m => m.OrganizationId == id).Role))
            .ToList();
        var whIds = warehouses.Select(w => w.Id).ToList();
        return new OrganizationDetailsDto(org.Id, org.Name, org.Type, org.Inn, org.City, org.Status, org.CreatedAt, users,
            warehouses.Select(w => new OrganizationWarehouseDto(w.Id, w.Name, w.Code, w.City)).ToList(),
            contracts.Select(c =>
            {
                var partner = isFc ? c.SellerId : c.FulfillmentId;
                return new OrganizationPartnerDto(partner, names.GetValueOrDefault(partner) ?? "—", c.StartedAt, c.WarehouseIds.Count);
            }).ToList(),
            await metrics.CountOrders30dAsync(clock.UtcNow, isFc ? null : org.Id, isFc ? whIds : null, ct),
            await metrics.CountProductsAsync(org.Id, ct));
    }
}
