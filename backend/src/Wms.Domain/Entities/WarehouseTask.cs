using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Позиция складского задания: товар, ячейка, количество и отсканированный прогресс.</summary>
public sealed class TaskItem
{
    private TaskItem() { }

    public TaskItem(string productId, string locationId, int quantity, int progress = 0)
    {
        ProductId = productId; LocationId = locationId; Quantity = quantity; Progress = progress;
    }

    public string ProductId { get; private set; } = null!;
    public string LocationId { get; private set; } = null!;
    public int Quantity { get; private set; }
    public int Progress { get; private set; }

    internal void Finish() => Progress = Quantity;
}

/// <summary>Складское задание (приёмка, размещение, отбор…).</summary>
public sealed class WarehouseTask
{
    private WarehouseTask() { }

    public WarehouseTask(string id, string number, TaskType type, string warehouseId, string organizationId,
        DateTimeOffset createdAt, IEnumerable<TaskItem> items, string? inboundId = null, TaskPriority priority = TaskPriority.Normal)
    {
        Id = id; Number = number; Type = type; WarehouseId = warehouseId; OrganizationId = organizationId;
        CreatedAt = createdAt; Items = items.ToList(); InboundId = inboundId; Priority = priority;
        Status = WarehouseTaskStatus.New;
    }

    public string Id { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public TaskType Type { get; private set; }
    public WarehouseTaskStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public string WarehouseId { get; private set; } = null!;
    public string OrganizationId { get; private set; } = null!;
    public string? AssigneeId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public string? InboundId { get; private set; }
    public string Algorithm { get; private set; } = "optimized";
    public List<TaskItem> Items { get; private set; } = [];

    public bool IsOpen => Status is not (WarehouseTaskStatus.Done or WarehouseTaskStatus.Cancelled);

    /// <summary>Номер задания как в моках: T- + последние 6 цифр (100000 + seq·7).</summary>
    public static string NumberFor(long seq) => "T-" + (100000 + seq * 7).ToString(System.Globalization.CultureInfo.InvariantCulture)[^6..];

    public void Start(DateTimeOffset now)
    {
        if (!IsOpen) throw new DomainConflictException("Задание уже закрыто");
        Status = WarehouseTaskStatus.InProgress;
        StartedAt ??= now;
    }

    public void Complete(DateTimeOffset now)
    {
        if (!IsOpen) throw new DomainConflictException("Задание уже закрыто");
        StartedAt ??= now;
        Status = WarehouseTaskStatus.Done;
        FinishedAt = now;
        foreach (var item in Items) item.Finish();
    }

    public void Cancel(DateTimeOffset now)
    {
        if (!IsOpen) return;
        Status = WarehouseTaskStatus.Cancelled;
        FinishedAt = now;
    }
}
