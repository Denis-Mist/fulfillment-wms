using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class User
{
    private readonly List<Membership> _memberships = [];

    private User() { }

    public User(string id, string email, string passwordHash, string fullName, string? phone, bool isPlatformAdmin)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new DomainException("Не задан идентификатор пользователя");
        if (string.IsNullOrWhiteSpace(email)) throw new DomainException("Не задан email");
        Id = id;
        Email = NormalizeEmail(email);
        PasswordHash = passwordHash;
        FullName = fullName.Trim();
        Phone = phone;
        IsPlatformAdmin = isPlatformAdmin;
    }

    public string Id { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string? Phone { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public IReadOnlyList<Membership> Memberships => _memberships;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public void JoinOrganization(Organization organization, Role role)
    {
        if (!RoleRules.IsAllowedFor(role, organization.Type))
            throw new DomainException($"Роль {role} недопустима для организации типа {organization.Type}");
        if (_memberships.Any(m => m.OrganizationId == organization.Id))
            throw new DomainException("Пользователь уже состоит в организации");
        _memberships.Add(new Membership(Id, organization.Id, role));
    }

    /// <summary>Членство по заголовку X-Organization-Id, иначе первое (как в моках).</summary>
    public Membership? ResolveMembership(string? organizationId) =>
        _memberships.FirstOrDefault(m => m.OrganizationId == organizationId) ?? _memberships.FirstOrDefault();
}
