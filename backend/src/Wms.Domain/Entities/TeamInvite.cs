using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class TeamInvite
{
    private TeamInvite() { }

    public TeamInvite(string id, string organizationId, string email, Role role, DateTimeOffset invitedAt)
    {
        Id = id; OrganizationId = organizationId; Email = User.NormalizeEmail(email); Role = role; InvitedAt = invitedAt;
    }

    public string Id { get; private set; } = null!;
    public string OrganizationId { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public Role Role { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public bool Revoked { get; private set; }

    public void Revoke() => Revoked = true;
}
