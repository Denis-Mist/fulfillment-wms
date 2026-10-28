using Wms.Application.Abstractions;
using Wms.Domain.Entities;

namespace Wms.Application.Auth;

public interface ICurrentUserMapper
{
    Task<CurrentUserDto> MapAsync(User user, CancellationToken ct);
}

/// <summary>Аналог toCurrentUser из моков: членства без организации отбрасываются.</summary>
public sealed class CurrentUserMapper(IOrganizationRepository organizations) : ICurrentUserMapper
{
    public async Task<CurrentUserDto> MapAsync(User user, CancellationToken ct)
    {
        var orgs = (await organizations.GetByIdsAsync(user.Memberships.Select(m => m.OrganizationId), ct))
            .ToDictionary(o => o.Id);
        var memberships = user.Memberships
            .Where(m => orgs.ContainsKey(m.OrganizationId))
            .Select(m =>
            {
                var org = orgs[m.OrganizationId];
                return new MembershipDto(org.Id, org.Name, org.Type, org.Status, m.Role);
            })
            .ToList();
        return new CurrentUserDto(user.Id, user.Email, user.FullName, user.Phone, user.IsPlatformAdmin, memberships);
    }
}
