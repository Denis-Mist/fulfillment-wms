using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Запись журнала действий (неизменяемая).</summary>
public class AuditEntry
{
    private AuditEntry() { }

    public AuditEntry(string id, DateTimeOffset at, AuditCategory category, string action, string? details,
        string actorName, string? organizationId, string? entity)
    {
        Id = id; At = at; Category = category; Action = action; Details = details;
        ActorName = actorName; OrganizationId = organizationId; Entity = entity;
    }

    public string Id { get; private set; } = null!;
    public DateTimeOffset At { get; private set; }
    public AuditCategory Category { get; private set; }
    public string Action { get; private set; } = null!;
    public string? Details { get; private set; }
    public string ActorName { get; private set; } = null!;
    public string? OrganizationId { get; private set; }
    public string? Entity { get; private set; }
}
