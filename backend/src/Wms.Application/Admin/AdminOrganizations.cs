using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Common;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Admin;

public sealed record AdminOrganizationDto(string Id, string Name, OrganizationType Type, string Inn, string City,
    OrganizationStatus Status, DateTimeOffset CreatedAt, string OwnerName, string OwnerEmail);

public sealed record AdminOrganizationSummary(int Total, IReadOnlyDictionary<string, int> ByStatus);

public sealed record AdminOrganizationQuery(PageQuery Page, OrganizationType? Type, OrganizationStatus? Status);

/// <summary>Владелец организации (fc_owner/seller_owner) — для карточек модерации.</summary>
public interface IOrganizationOwnerLookup
{
    Task<IReadOnlyDictionary<string, User>> GetOwnersAsync(CancellationToken ct);
}

public sealed class OrganizationOwnerLookup(IAppDbContext db) : IOrganizationOwnerLookup
{
    public async Task<IReadOnlyDictionary<string, User>> GetOwnersAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);
        var owners = new Dictionary<string, User>();
        foreach (var user in users)
        foreach (var m in user.Memberships.Where(m => m.Role is Role.FcOwner or Role.SellerOwner))
            owners.TryAdd(m.OrganizationId, user);
        return owners;
    }
}

public static class AdminOrganizationMapper
{
    public static AdminOrganizationDto ToDto(Organization o, IReadOnlyDictionary<string, User> owners)
    {
        owners.TryGetValue(o.Id, out var owner);
        return new AdminOrganizationDto(o.Id, o.Name, o.Type, o.Inn, o.City, o.Status, o.CreatedAt,
            owner?.FullName ?? "—", owner?.Email ?? "—");
    }
}

/// <summary>Список/сводка организаций для модерации (фильтры как в моках).</summary>
public sealed class AdminOrganizationsQueryHandler(IAppDbContext db, IOrganizationOwnerLookup ownerLookup)
{
    public async Task<Paged<AdminOrganizationDto>> ListAsync(AdminOrganizationQuery query, CancellationToken ct)
    {
        var items = await FilteredAsync(query.Page, query.Type, query.Status, ct);
        var order = query.Page.OrderOr(SortOrder.Desc);
        var sorted = items.SortBy(SortKey(query.Page.SortField ?? "createdAt"), order);
        return Paged<AdminOrganizationDto>.From(sorted, query.Page);
    }

    public async Task<AdminOrganizationSummary> SummaryAsync(PageQuery page, OrganizationType? type, CancellationToken ct)
    {
        var items = await FilteredAsync(page, type, null, ct);
        var byStatus = items.GroupBy(i => SnakeCaseName.Of(i.Status)).ToDictionary(g => g.Key, g => g.Count());
        return new AdminOrganizationSummary(items.Count, byStatus);
    }

    private async Task<IReadOnlyList<AdminOrganizationDto>> FilteredAsync(PageQuery page, OrganizationType? type,
        OrganizationStatus? status, CancellationToken ct)
    {
        var owners = await ownerLookup.GetOwnersAsync(ct);
        var orgs = db.Organizations.AsNoTracking();
        if (type is not null) orgs = orgs.Where(o => o.Type == type);
        if (status is not null) orgs = orgs.Where(o => o.Status == status);
        var term = page.Term;
        return (await orgs.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct))
            .Select(o => AdminOrganizationMapper.ToDto(o, owners))
            .Where(o => term.Length == 0 || o.Name.ToLowerInvariant().Contains(term) || o.Inn.Contains(term)
                || o.OwnerEmail.ToLowerInvariant().Contains(term))
            .ToList();
    }

    private static Func<AdminOrganizationDto, object?>? SortKey(string field) => field switch
    {
        "name" => o => o.Name,
        "type" => o => SnakeCaseName.Of(o.Type),
        "inn" => o => o.Inn,
        "city" => o => o.City,
        "status" => o => SnakeCaseName.Of(o.Status),
        "createdAt" => o => o.CreatedAt,
        "ownerName" => o => o.OwnerName,
        "ownerEmail" => o => o.OwnerEmail,
        _ => null,
    };
}

/// <summary>Модерация организации: approve / reject / block / unblock.</summary>
public sealed class ModerateOrganizationHandler(IAppDbContext db, IOrganizationOwnerLookup ownerLookup)
{
    public async Task<AdminOrganizationDto> HandleAsync(string id, string action, CancellationToken ct)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw AppException.NotFound("Организация не найдена");
        if (!SnakeCaseName.TryParse<ModerationAction>(action, out var parsed))
            throw AppException.BadRequest("Неизвестное действие");
        try { org.Moderate(parsed); }
        catch (DomainConflictException e) { throw AppException.Conflict(e.Message); }
        // Организация, зарегистрированная через /auth/register, получает настройки при первом одобрении
        if (org.IsActive && !await db.Settings.AnyAsync(s => s.OrganizationId == org.Id, ct))
            db.Settings.Add(Settings.DefaultSettings.For(org));
        await db.SaveChangesAsync(ct);
        return AdminOrganizationMapper.ToDto(org, await ownerLookup.GetOwnersAsync(ct));
    }
}
