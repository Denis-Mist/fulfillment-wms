using Microsoft.EntityFrameworkCore;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Notifications;

public sealed record AppNotification(string Id, string Title, string Text, DateTimeOffset At, bool Read, AttentionTone Tone, NotificationLink? Link);

/// <summary>Администратору — организации, ожидающие проверки.</summary>
public sealed class PendingOrganizationsNotificationSource(IAppDbContext db) : INotificationSource
{
    public async Task<IReadOnlyList<NotificationItem>> GetAsync(RequestContext ctx, DateTimeOffset now, CancellationToken ct)
    {
        if (!ctx.IsPlatformAdmin) return [];
        return (await db.Organizations.AsNoTracking().Where(o => o.Status == OrganizationStatus.Pending).ToListAsync(ct))
            .Select(o => new NotificationItem($"pending_{o.Id}", "Новая заявка на регистрацию",
                $"{o.Name} ({o.City}) ожидает проверки", o.CreatedAt, AttentionTone.Warn, null))
            .ToList();
    }
}

/// <summary>Собирает уведомления всех источников, отмечает прочитанные, сортирует от новых к старым.</summary>
public sealed class NotificationsHandler(IAppDbContext db, IEnumerable<INotificationSource> sources, IClock clock)
{
    public async Task<IReadOnlyList<AppNotification>> ListAsync(RequestContext ctx, CancellationToken ct)
    {
        var items = await BuildAsync(ctx, ct);
        var read = (await db.NotificationReads.AsNoTracking().Where(r => r.UserId == ctx.UserId)
            .Select(r => r.NotificationId).ToListAsync(ct)).ToHashSet();
        return items.Select(n => new AppNotification(n.Id, n.Title, n.Text, n.At, read.Contains(n.Id), n.Tone, n.Link)).ToList();
    }

    public async Task MarkAllReadAsync(RequestContext ctx, CancellationToken ct)
    {
        var ids = (await BuildAsync(ctx, ct)).Select(n => n.Id).ToList();
        var existing = (await db.NotificationReads.Where(r => r.UserId == ctx.UserId && ids.Contains(r.NotificationId))
            .Select(r => r.NotificationId).ToListAsync(ct)).ToHashSet();
        foreach (var id in ids.Where(id => !existing.Contains(id)).Distinct())
            db.NotificationReads.Add(new NotificationRead(ctx.UserId, id, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<NotificationItem>> BuildAsync(RequestContext ctx, CancellationToken ct)
    {
        var all = new List<NotificationItem>();
        foreach (var source in sources) all.AddRange(await source.GetAsync(ctx, clock.UtcNow, ct));
        return all.OrderByDescending(n => n.At).ToList();
    }
}
