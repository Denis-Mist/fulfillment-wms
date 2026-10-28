namespace Wms.Domain.Enums;

public static class RoleRules
{
    public static readonly IReadOnlyList<Role> FulfillmentRoles =
    [
        Role.FcOwner, Role.FcManager, Role.FcSupervisor, Role.FcAccountManager,
        Role.FcReceiver, Role.FcPicker, Role.FcPacker,
    ];

    public static readonly IReadOnlyList<Role> SellerRoles = [Role.SellerOwner, Role.SellerManager];

    public static readonly IReadOnlyList<Role> WorkerRoles = [Role.FcReceiver, Role.FcPicker, Role.FcPacker];

    /// <summary>Роль владельца, которую получает зарегистрировавший организацию.</summary>
    public static Role OwnerRoleFor(OrganizationType type) =>
        type == OrganizationType.Seller ? Role.SellerOwner : Role.FcOwner;

    public static bool IsAllowedFor(Role role, OrganizationType type) =>
        type == OrganizationType.Seller ? SellerRoles.Contains(role) : FulfillmentRoles.Contains(role);
}
