using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Admin;

/// <summary>Запись действий администратора в журнал (без сохранения — вызывающий сценарий фиксирует единицу работы).</summary>
public interface IAuditLog
{
    Task AppendAsync(RequestContext ctx, AuditCategory category, string action, string? details, string? entity, CancellationToken ct);
}

public sealed class AuditLog(IAppDbContext db, IClock clock, IIdGenerator ids) : IAuditLog
{
    public async Task AppendAsync(RequestContext ctx, AuditCategory category, string action, string? details, string? entity, CancellationToken ct)
    {
        var actor = await db.Users.AsNoTracking().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "—";
        db.AuditEntries.Add(new AuditEntry($"aud_{ids.NewSuffix()}", clock.UtcNow, category, action, details, actor, null, entity));
    }
}

/// <summary>Регистрации организаций — производные события журнала.</summary>
public sealed class OrganizationRegistrationAuditSource(IAppDbContext db) : IAuditEventSource
{
    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(DateTimeOffset now, CancellationToken ct) =>
        (await db.Organizations.AsNoTracking().ToListAsync(ct))
            .Select(o => new AuditEventDto($"aud_org_{o.Id}", o.CreatedAt, AuditCategory.Organization, "Регистрация организации",
                $"ИНН {o.Inn}, {o.City}", "Система", o.Id, o.Name, o.Name))
            .ToList();
}

/// <summary>Сохранённые действия администраторов.</summary>
public sealed class StoredAuditSource(IAppDbContext db) : IAuditEventSource
{
    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var names = await db.Organizations.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return (await db.AuditEntries.AsNoTracking().ToListAsync(ct))
            .Select(e => new AuditEventDto(e.Id, e.At, e.Category, e.Action, e.Details, e.ActorName, e.OrganizationId,
                e.OrganizationId is null ? null : names.GetValueOrDefault(e.OrganizationId), e.Entity))
            .ToList();
    }
}

/// <summary>Объединённый журнал из всех источников.</summary>
public interface IAuditFeed
{
    Task<IReadOnlyList<AuditEventDto>> AllAsync(CancellationToken ct);
}

public sealed class AuditFeed(IEnumerable<IAuditEventSource> sources, IClock clock) : IAuditFeed
{
    public async Task<IReadOnlyList<AuditEventDto>> AllAsync(CancellationToken ct)
    {
        var result = new List<AuditEventDto>();
        foreach (var source in sources) result.AddRange(await source.GetEventsAsync(clock.UtcNow, ct));
        return result;
    }
}

public sealed record AuditQuery(PageQuery Page, AuditCategory? Category, string? OrganizationId);

public sealed class AuditQueryHandler(IAuditFeed feed)
{
    public async Task<Paged<AuditEventDto>> HandleAsync(AuditQuery query, CancellationToken ct)
    {
        var term = query.Page.Term;
        var items = (await feed.AllAsync(ct)).Where(e =>
            (query.Category is null || e.Category == query.Category) &&
            (string.IsNullOrEmpty(query.OrganizationId) || e.OrganizationId == query.OrganizationId) &&
            (term.Length == 0 || new[] { e.Action, e.Details, e.ActorName, e.Entity }.Any(v => v?.ToLowerInvariant().Contains(term) == true)));
        return Paged<AuditEventDto>.From(items.SortBy(e => e.At, query.Page.OrderOr(SortOrder.Desc)), query.Page);
    }
}
