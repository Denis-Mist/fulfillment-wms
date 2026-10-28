using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Admin;

public sealed record PlatformUserMembership(string OrganizationId, string OrganizationName, Role Role);

public sealed record PlatformUserDto(string Id, string FullName, string Email, string? Phone, bool IsPlatformAdmin,
    bool Blocked, IReadOnlyList<PlatformUserMembership> Memberships);

public sealed record PlatformUserQuery(PageQuery Page, Role? Role, string? State);

public sealed record TemporaryPasswordDto(string TemporaryPassword);

internal static class PlatformUserMapper
{
    public static PlatformUserDto ToDto(User u, IReadOnlyDictionary<string, string> orgNames) =>
        new(u.Id, u.FullName, u.Email, u.Phone, u.IsPlatformAdmin, u.IsBlocked,
            u.Memberships.Select(m => new PlatformUserMembership(m.OrganizationId,
                orgNames.GetValueOrDefault(m.OrganizationId) ?? "—", m.Role)).ToList());

    public static async Task<IReadOnlyDictionary<string, string>> OrgNamesAsync(IAppDbContext db, CancellationToken ct) =>
        await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
}

public sealed class PlatformUsersQueryHandler(IAppDbContext db)
{
    public async Task<Paged<PlatformUserDto>> HandleAsync(PlatformUserQuery query, CancellationToken ct)
    {
        var names = await PlatformUserMapper.OrgNamesAsync(db, ct);
        var term = query.Page.Term;
        var items = (await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct))
            .Select(u => PlatformUserMapper.ToDto(u, names))
            .Where(u => query.Role is null || (query.Role == Role.PlatformAdmin ? u.IsPlatformAdmin : u.Memberships.Any(m => m.Role == query.Role)))
            .Where(u => string.IsNullOrEmpty(query.State) || (query.State == "blocked") == u.Blocked)
            .Where(u => term.Length == 0 || u.FullName.ToLowerInvariant().Contains(term) || u.Email.ToLowerInvariant().Contains(term)
                || u.Memberships.Any(m => m.OrganizationName.ToLowerInvariant().Contains(term)));
        var sorted = items.SortBy(SortKey(query.Page.SortField ?? "fullName"), query.Page.OrderOr(SortOrder.Asc));
        return Paged<PlatformUserDto>.From(sorted, query.Page);
    }

    private static Func<PlatformUserDto, object?>? SortKey(string field) => field switch
    {
        "fullName" => u => u.FullName,
        "email" => u => u.Email,
        "phone" => u => u.Phone,
        "isPlatformAdmin" => u => u.IsPlatformAdmin,
        "blocked" => u => u.Blocked,
        _ => null,
    };
}

/// <summary>Действия администратора с пользователем: reset-password / block / unblock (с записью в журнал).</summary>
public sealed class PlatformUserActionHandler(IAppDbContext db, IAuditLog audit, IPasswordHasher hasher,
    ITemporaryPasswordGenerator passwords)
{
    public async Task<object> HandleAsync(RequestContext ctx, string id, string action, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw AppException.NotFound("Пользователь не найден");
        if (action == "reset-password")
        {
            var password = passwords.Generate();
            user.ChangePasswordHash(hasher.Hash(password));
            await audit.AppendAsync(ctx, AuditCategory.User, "Сброс пароля", user.Email, user.FullName, ct);
            await db.SaveChangesAsync(ct);
            return new TemporaryPasswordDto(password);
        }
        if (action is not ("block" or "unblock")) throw AppException.BadRequest("Неизвестное действие");
        try
        {
            if (action == "block") user.Block(); else user.Unblock();
        }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        await audit.AppendAsync(ctx, AuditCategory.User,
            action == "block" ? "Пользователь заблокирован" : "Пользователь разблокирован", user.Email, user.FullName, ct);
        await db.SaveChangesAsync(ct);
        return PlatformUserMapper.ToDto(user, await PlatformUserMapper.OrgNamesAsync(db, ct));
    }
}
