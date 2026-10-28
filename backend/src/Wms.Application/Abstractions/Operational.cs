using Wms.Application.Auth;
using Wms.Domain.Enums;

namespace Wms.Application.Abstractions;

/// <summary>Каналы продаж заказов (SalesChannel во фронте).</summary>
public sealed record ChannelCounts(int Wb, int Ozon, int Own);

public sealed record PlatformOrderStats(int Orders30d, int OrdersPrev30d, int Shipped30d, int OnTimeShipped30d, int Inbound30d,
    IReadOnlyDictionary<DateOnly, ChannelCounts> OrdersByDay, IReadOnlyDictionary<string, int> Orders30dByWarehouse);

/// <summary>
/// Порт к операционным данным (заказы, поставки, товары, остатки). Эти модули переносятся в следующих PR;
/// до тех пор используется реализация, возвращающая нули.
/// </summary>
public interface IOperationalMetrics
{
    Task<PlatformOrderStats> GetPlatformStatsAsync(DateTimeOffset now, CancellationToken ct);
    /// <summary>Заказы за 30 дней: селлера (sellerId) и/или по складам (warehouseIds).</summary>
    Task<int> CountOrders30dAsync(DateTimeOffset now, string? sellerId, IReadOnlyCollection<string>? warehouseIds, CancellationToken ct);
    Task<int> CountProductsAsync(string sellerId, CancellationToken ct);
    Task<int> StockUnitsAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct);
    /// <summary>Количество заказов по дням (начала суток → число).</summary>
    Task<IReadOnlyList<int>> OrdersTrendAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, IReadOnlyList<DateTimeOffset> dayStarts, CancellationToken ct);
    Task<int> ActiveInboundAsync(string sellerId, IReadOnlyCollection<string> warehouseIds, CancellationToken ct);
}

/// <summary>Событие журнала (AuditEvent во фронте).</summary>
public sealed record AuditEventDto(string Id, DateTimeOffset At, AuditCategory Category, string Action, string? Details,
    string ActorName, string? OrganizationId, string? OrganizationName, string? Entity);

/// <summary>Источник событий журнала (открыт для расширения: поставки, отгрузки — в следующих PR).</summary>
public interface IAuditEventSource
{
    Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(DateTimeOffset now, CancellationToken ct);
}

public enum AttentionTone { Info, Warn, Danger, Success }

public sealed record NotificationLink(string Type, string Id);

public sealed record NotificationItem(string Id, string Title, string Text, DateTimeOffset At, AttentionTone Tone, NotificationLink? Link);

/// <summary>Источник уведомлений для текущего контекста (организации на проверке, просрочки, поставки…).</summary>
public interface INotificationSource
{
    Task<IReadOnlyList<NotificationItem>> GetAsync(RequestContext ctx, DateTimeOffset now, CancellationToken ct);
}

public sealed record SearchResult(string Type, string Id, string Title, string Subtitle);

/// <summary>Провайдер глобального поиска (заказы, поставки, товары — в следующих PR).</summary>
public interface ISearchProvider
{
    int Order { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(RequestContext ctx, string term, CancellationToken ct);
}

/// <summary>Генератор временных паролей при сбросе администратором.</summary>
public interface ITemporaryPasswordGenerator
{
    string Generate();
}
