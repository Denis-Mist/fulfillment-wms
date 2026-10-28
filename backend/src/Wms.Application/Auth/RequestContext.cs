using Wms.Application.Abstractions;
using Wms.Domain.Enums;

namespace Wms.Application.Auth;

/// <summary>Кто делает запрос и от имени какой организации (X-Organization-Id).</summary>
public sealed record RequestContext(string UserId, string? OrganizationId, Role Role, OrganizationStatus? OrganizationStatus,
    OrganizationType? OrganizationType = null)
{
    public bool IsPlatformAdmin => OrganizationId is null;
    public bool IsFulfillment => OrganizationType == Domain.Enums.OrganizationType.Fulfillment;
    public bool IsSeller => OrganizationType == Domain.Enums.OrganizationType.Seller;

    /// <summary>Как withAuth в моках: организация «не активна» закрывает все разделы, кроме разрешённых явно.</summary>
    public bool OrganizationInactive => OrganizationId is not null && OrganizationStatus != Domain.Enums.OrganizationStatus.Active;

}

public interface IRequestContextResolver
{
    /// <returns>null — у пользователя нет доступа ни к одной организации.</returns>
    Task<RequestContext?> ResolveAsync(string userId, string? organizationIdHeader, CancellationToken ct);
}

public sealed class RequestContextResolver(IUserRepository users, IOrganizationRepository organizations) : IRequestContextResolver
{
    public async Task<RequestContext?> ResolveAsync(string userId, string? organizationIdHeader, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct);
        if (user is null) return null;
        if (user.IsPlatformAdmin) return new RequestContext(user.Id, null, Role.PlatformAdmin, null);
        var membership = user.ResolveMembership(organizationIdHeader);
        if (membership is null) return null;
        var org = (await organizations.GetByIdsAsync([membership.OrganizationId], ct)).FirstOrDefault();
        return new RequestContext(user.Id, membership.OrganizationId, membership.Role, org?.Status, org?.Type);
    }
}
