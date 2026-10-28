using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Auth;

public sealed class RegisterHandler(
    IRegisterRequestValidator validator,
    IUserRepository users,
    IOrganizationRepository organizations,
    IUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    ITokenService tokens,
    ICurrentUserMapper mapper,
    IClock clock,
    IIdGenerator ids)
{
    public async Task<AuthResponse> HandleAsync(RegisterRequest request, CancellationToken ct)
    {
        var errors = validator.Validate(request);
        if (errors.Count > 0) throw AppException.BadRequest("Заполнены не все обязательные поля", errors: errors);

        var email = User.NormalizeEmail(request.User!.Email!);
        if (await users.EmailExistsAsync(email, ct))
            throw AppException.Conflict("Email уже зарегистрирован", "Войдите в систему или используйте другой email.");

        var suffix = ids.NewSuffix();
        var org = Organization.Register($"org_r_{suffix}", request.OrganizationType, request.Organization!.Name!,
            request.Organization.Inn ?? string.Empty, request.Organization.City ?? string.Empty, clock.UtcNow,
            request.Organization.Marketplaces);
        var user = new User($"usr_r_{suffix}", email, hasher.Hash(request.User.Password!), request.User.FullName!,
            request.User.Phone, isPlatformAdmin: false);
        user.JoinOrganization(org, RoleRules.OwnerRoleFor(org.Type));

        organizations.Add(org);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(ct);

        return new AuthResponse(tokens.CreateAccessToken(user), await mapper.MapAsync(user, ct));
    }
}
