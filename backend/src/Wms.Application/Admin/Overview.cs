using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Admin;

public sealed record OrganizationCounts(int Total, int Fulfillment, int Seller, int Pending, int Blocked);
public sealed record UserCounts(int Total, int Blocked);
public sealed record OrdersByDay(string Date, ChannelCounts Channels);
public sealed record TopFulfillment(string Id, string Name, int Warehouses, int Clients, int Orders30d);

public sealed record PlatformOverview(OrganizationCounts Organizations, UserCounts Users, int Warehouses, int Orders30d,
    int OrdersPrev30d, int Shipped30d, int Inbound30d, double OnTimeRate, IReadOnlyList<OrdersByDay> OrdersByDay,
    IReadOnlyList<TopFulfillment> TopFulfillments, IReadOnlyList<AuditEventDto> RecentEvents);

public sealed class PlatformOverviewHandler(IAppDbContext db, IOperationalMetrics metrics, IAuditFeed audit, IClock clock)
{
    public async Task<PlatformOverview> HandleAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var orgs = await db.Organizations.AsNoTracking().OrderBy(o => o.CreatedAt).ToListAsync(ct);
        var warehouses = await db.Warehouses.AsNoTracking().ToListAsync(ct);
        var contracts = await db.Contracts.AsNoTracking().ToListAsync(ct);
        var stats = await metrics.GetPlatformStatsAsync(now, ct);

        var byDay = Enumerable.Range(0, 30).Reverse()
            .Select(i => DateOnly.FromDateTime(now.UtcDateTime.AddDays(-i)))
            .Select(d => new OrdersByDay(d.ToString("yyyy-MM-dd"), stats.OrdersByDay.GetValueOrDefault(d) ?? new ChannelCounts(0, 0, 0)))
            .ToList();

        var top = orgs.Where(o => o.Type == OrganizationType.Fulfillment && o.Status == OrganizationStatus.Active)
            .Select(fc =>
            {
                var own = warehouses.Where(w => w.OrganizationId == fc.Id).Select(w => w.Id).ToList();
                return new TopFulfillment(fc.Id, fc.Name, own.Count, contracts.Count(c => c.FulfillmentId == fc.Id),
                    own.Sum(w => stats.Orders30dByWarehouse.GetValueOrDefault(w)));
            })
            .OrderByDescending(t => t.Orders30d).Take(5).ToList();

        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        return new PlatformOverview(
            new OrganizationCounts(orgs.Count, orgs.Count(o => o.Type == OrganizationType.Fulfillment),
                orgs.Count(o => o.Type == OrganizationType.Seller), orgs.Count(o => o.Status == OrganizationStatus.Pending),
                orgs.Count(o => o.Status == OrganizationStatus.Blocked)),
            new UserCounts(users.Count, users.Count(u => u.IsBlocked)),
            warehouses.Count, stats.Orders30d, stats.OrdersPrev30d, stats.Shipped30d, stats.Inbound30d,
            stats.Shipped30d == 0 ? 1 : (double)stats.OnTimeShipped30d / stats.Shipped30d,
            byDay, top,
            (await audit.AllAsync(ct)).SortBy(e => e.At, SortOrder.Desc).Take(8).ToList());
    }
}
