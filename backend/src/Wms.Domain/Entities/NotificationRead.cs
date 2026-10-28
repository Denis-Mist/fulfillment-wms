namespace Wms.Domain.Entities;

/// <summary>Отметка «уведомление прочитано» пользователем.</summary>
public class NotificationRead
{
    private NotificationRead() { }

    public NotificationRead(string userId, string notificationId, DateTimeOffset readAt)
    {
        UserId = userId; NotificationId = notificationId; ReadAt = readAt;
    }

    public string UserId { get; private set; } = null!;
    public string NotificationId { get; private set; } = null!;
    public DateTimeOffset ReadAt { get; private set; }
}
