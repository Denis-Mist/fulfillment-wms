using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Domain.Entities;

namespace Wms.Application.Auth;

public sealed class LoginHandler(IUserRepository users, IPasswordHasher hasher, ITokenService tokens, ICurrentUserMapper mapper)
{
    public async Task<AuthResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email ?? string.Empty);
        var user = email.Length == 0 ? null : await users.FindByEmailAsync(email, ct);
        if (user is null || !hasher.Verify(user.PasswordHash, request.Password ?? string.Empty))
            throw AppException.Unauthorized("Неверный email или пароль", "Проверьте данные и попробуйте ещё раз.");
        return new AuthResponse(tokens.CreateAccessToken(user), await mapper.MapAsync(user, ct));
    }
}
