using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(WmsDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<User?> FindByIdAsync(string id, CancellationToken ct) => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class OrganizationRepository(WmsDbContext db) : IOrganizationRepository
{
    public async Task<IReadOnlyList<Organization>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Organizations.Where(o => list.Contains(o.Id)).ToListAsync(ct);
    }

    public void Add(Organization organization) => db.Organizations.Add(organization);
}
