using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Application.Demo;
using Wms.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Wms.Application.Auth;

public sealed class DemoLoginHandler(
    IOptions<DemoOptions> options,
    IUserRepository users,
    ITokenService tokens,
    ICurrentUserMapper mapper)
{
    public async Task<AuthResponse> HandleAsync(DemoLoginRequest request, CancellationToken ct)
    {
        if (!options.Value.Enabled) throw AppException.NotFound("Не найдено");
        if (!DemoPersonas.TryGetEmail(request.Persona, out var email))
            throw AppException.Unprocessable("Неизвестная персона");

        var user = await users.FindByEmailAsync(User.NormalizeEmail(email), ct)
            ?? throw AppException.NotFound("Пользователь не найден");

        var lifetime = Math.Max(1, options.Value.TokenLifetimeMinutes);
        var token = tokens.CreateAccessToken(user, new AccessTokenRequest(lifetime,
            new Dictionary<string, string> { [DemoClaims.Type] = DemoClaims.Value }));
        return new AuthResponse(token, await mapper.MapAsync(user, ct, isDemo: true));
    }
}
