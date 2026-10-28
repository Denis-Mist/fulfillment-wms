using Wms.Application.Abstractions;
using Wms.Application.Common;

namespace Wms.Application.Auth;

public sealed class GetCurrentUserHandler(IUserRepository users, ICurrentUserMapper mapper)
{
    public async Task<CurrentUserDto> HandleAsync(string userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct) ?? throw AppException.Unauthorized();
        return await mapper.MapAsync(user, ct);
    }
}
