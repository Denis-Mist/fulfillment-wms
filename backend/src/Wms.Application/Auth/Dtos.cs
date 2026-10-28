using Wms.Domain.Enums;

namespace Wms.Application.Auth;

public record LoginRequest(string? Email, string? Password);

public record RegisterOrganization(string? Name, string? Inn, string? City, int? WarehousesCount, List<Marketplace>? Marketplaces);

public record RegisterUser(string? FullName, string? Email, string? Phone, string? Password);

public record RegisterRequest(OrganizationType OrganizationType, RegisterOrganization? Organization, RegisterUser? User);

public record MembershipDto(string OrganizationId, string OrganizationName, OrganizationType OrganizationType,
    OrganizationStatus OrganizationStatus, Role Role);

public record CurrentUserDto(string Id, string Email, string FullName, string? Phone, bool IsPlatformAdmin,
    IReadOnlyList<MembershipDto> Memberships);

public record AuthResponse(string AccessToken, CurrentUserDto User);
