using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class Organization
{
    private Organization() { }

    public Organization(string id, OrganizationType type, string name, string inn, string city,
        OrganizationStatus status, DateTimeOffset createdAt, IEnumerable<Marketplace>? marketplaces = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new DomainException("Не задан идентификатор организации");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Не задано название организации");
        Id = id;
        Type = type;
        Name = name.Trim();
        Inn = inn?.Trim() ?? string.Empty;
        City = city?.Trim() ?? string.Empty;
        Status = status;
        CreatedAt = createdAt;
        Marketplaces = marketplaces?.Distinct().ToList();
    }

    public string Id { get; private set; } = null!;
    public OrganizationType Type { get; private set; }
    public string Name { get; private set; } = null!;
    public string Inn { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public OrganizationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public List<Marketplace>? Marketplaces { get; private set; }

    /// <summary>Новая организация после самостоятельной регистрации ждёт модерации.</summary>
    public static Organization Register(string id, OrganizationType type, string name, string inn, string city,
        DateTimeOffset now, IEnumerable<Marketplace>? marketplaces) =>
        new(id, type, name, inn, city, OrganizationStatus.Pending, now, marketplaces);

    private static readonly IReadOnlyDictionary<ModerationAction, (OrganizationStatus[] From, OrganizationStatus To)> Transitions =
        new Dictionary<ModerationAction, (OrganizationStatus[], OrganizationStatus)>
        {
            [ModerationAction.Approve] = ([OrganizationStatus.Pending, OrganizationStatus.Rejected], OrganizationStatus.Active),
            [ModerationAction.Reject] = ([OrganizationStatus.Pending], OrganizationStatus.Rejected),
            [ModerationAction.Block] = ([OrganizationStatus.Active], OrganizationStatus.Blocked),
            [ModerationAction.Unblock] = ([OrganizationStatus.Blocked], OrganizationStatus.Active),
        };

    public bool IsActive => Status == OrganizationStatus.Active;

    /// <summary>Модерация администратором платформы (правила переходов — как в моках).</summary>
    public void Moderate(ModerationAction action)
    {
        var (from, to) = Transitions[action];
        if (!from.Contains(Status)) throw new DomainConflictException("Действие недоступно для текущего статуса");
        Status = to;
    }
}
