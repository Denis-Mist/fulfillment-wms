using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class Membership
{
    private Membership() { }

    internal Membership(string userId, string organizationId, Role role)
    {
        UserId = userId;
        OrganizationId = organizationId;
        Role = role;
    }

    public string UserId { get; private set; } = null!;
    public string OrganizationId { get; private set; } = null!;
    public Role Role { get; private set; }
    public Organization? Organization { get; private set; }
}
