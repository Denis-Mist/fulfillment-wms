using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Settings;

public sealed record TeamMemberDto(string Id, string FullName, string Email, Role Role, string Status, DateTimeOffset? InvitedAt);

public sealed record InviteInput(string? Email, string? Role);

public sealed class TeamQueryHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<TeamMemberDto>> HandleAsync(RequestContext ctx, CancellationToken ct)
    {
        if (ctx.OrganizationId is null) return [];
        var orgId = ctx.OrganizationId;
        var pending = (await db.TeamInvites.AsNoTracking().Where(i => i.OrganizationId == orgId && !i.Revoked)
                .OrderBy(i => i.InvitedAt).ToListAsync(ct))
            .Select(i => new TeamMemberDto(i.Id, "", i.Email, i.Role, "invited", i.InvitedAt));
        var members = (await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct))
            .SelectMany(u => u.Memberships.Where(m => m.OrganizationId == orgId)
                .Select(m => new TeamMemberDto(u.Id, u.FullName, u.Email, m.Role, "active", null)));
        return [.. pending, .. members];
    }
}

public sealed partial class InviteMemberHandler(IAppDbContext db, IClock clock, IIdGenerator ids)
{
    [GeneratedRegex(@"^\S+@\S+\.\S+$")] private static partial Regex Email();

    public async Task<TeamMemberDto> HandleAsync(RequestContext ctx, InviteInput input, CancellationToken ct)
    {
        if (ctx.OrganizationId is null || ctx.OrganizationType is null) throw AppException.NotFound("Организация не найдена");
        var email = User.NormalizeEmail(input.Email ?? "");
        if (!Email().IsMatch(email)) throw AppException.Unprocessable("Некорректный email");
        if (!SnakeCaseName.TryParse<Role>(input.Role, out var role) || !RoleRules.IsAllowedFor(role, ctx.OrganizationType.Value))
            throw AppException.Unprocessable("Роль недоступна для организации");
        var orgId = ctx.OrganizationId;
        var taken = (await db.Users.AsNoTracking().Where(u => u.Email == email).ToListAsync(ct))
                .Any(u => u.Memberships.Any(m => m.OrganizationId == orgId))
            || await db.TeamInvites.AnyAsync(i => !i.Revoked && i.Email == email && i.OrganizationId == orgId, ct);
        if (taken) throw AppException.Conflict("Пользователь уже в команде или приглашён");
        var invite = new TeamInvite($"inv_{ids.NewSuffix()}", orgId, email, role, clock.UtcNow);
        db.TeamInvites.Add(invite);
        await db.SaveChangesAsync(ct);
        return new TeamMemberDto(invite.Id, "", invite.Email, invite.Role, "invited", invite.InvitedAt);
    }
}

public sealed class RevokeInviteHandler(IAppDbContext db)
{
    public async Task HandleAsync(RequestContext ctx, string id, CancellationToken ct)
    {
        var invite = await db.TeamInvites.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invite is null || invite.Revoked || invite.OrganizationId != ctx.OrganizationId)
            throw AppException.NotFound("Приглашение не найдено");
        invite.Revoke();
        await db.SaveChangesAsync(ct);
    }
}
