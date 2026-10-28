using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Services;

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

    public bool IsComplete => Progress >= Quantity;

    internal void Finish() => Progress = Quantity;

    internal void Tick() => Progress++;

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
    /// <summary>Заказы задания отбора/упаковки (батч).</summary>
    public List<string> OrderIds { get; private set; } = [];

    public const int MaxActivePerWorker = 3;

    /// <summary>Восстановление задания из снимка (сид): статус, исполнитель и прогресс как есть.</summary>
    public static WarehouseTask Restore(string id, string number, TaskType type, WarehouseTaskStatus status, TaskPriority priority,
        string warehouseId, string organizationId, string? assigneeId, DateTimeOffset createdAt, DateTimeOffset? startedAt,
        DateTimeOffset? finishedAt, IEnumerable<string> orderIds, string? inboundId, string algorithm, IEnumerable<TaskItem> items) =>
        new(id, number, type, warehouseId, organizationId, createdAt, items, inboundId, priority)
        {
            Status = status, AssigneeId = assigneeId, StartedAt = startedAt, FinishedAt = finishedAt,
            OrderIds = orderIds.ToList(), Algorithm = RouteAlgorithms.Parse(algorithm),
        };

    /// <summary>Новый лист отбора по батчу заказов (позиции уже упорядочены по маршруту).</summary>
    public static WarehouseTask PickingBatch(string id, string number, string warehouseId, string organizationId, DateTimeOffset now,
        TaskPriority priority, IEnumerable<string> orderIds, string algorithm, IEnumerable<TaskItem> items) =>
        new(id, number, TaskType.Picking, warehouseId, organizationId, now, items, null, priority)
        {
            OrderIds = orderIds.ToList(), Algorithm = RouteAlgorithms.Parse(algorithm),
        };

    /// <summary>Задание упаковки собранного заказа.</summary>
    public static WarehouseTask PackingFor(string id, string number, string warehouseId, string organizationId, DateTimeOffset now,
        TaskPriority priority, string orderId, IEnumerable<TaskItem> items) =>
        new(id, number, TaskType.Packing, warehouseId, organizationId, now, items, null, priority) { OrderIds = [orderId] };

    public int Units => Items.Sum(i => i.Quantity);
    public int DoneUnits => Items.Sum(i => i.Progress);

    public void ShiftTimes(TimeSpan delta)
    {
        CreatedAt += delta;
        StartedAt += delta;
        FinishedAt += delta;
    }

    /// <summary>Назначить исполнителя (null — снять). В работе исполнителя снять нельзя, статус «в работе» сохраняется.</summary>
    public void Assign(string? userId)
    {
        if (!IsOpen) throw new DomainConflictException("Задание уже закрыто");
        if (userId is null && Status == WarehouseTaskStatus.InProgress)
            throw new DomainConflictException("Нельзя снять исполнителя с задания в работе");
        AssigneeId = userId;
        if (Status != WarehouseTaskStatus.InProgress) Status = userId is null ? WarehouseTaskStatus.New : WarehouseTaskStatus.Assigned;
    }

    /// <summary>Отмена из диспетчерской: выполненное отменить нельзя, повторная отмена — без изменений.</summary>
    public void CancelByOffice()
    {
        if (Status == WarehouseTaskStatus.Done) throw new DomainConflictException("Выполненное задание нельзя отменить");
        if (Status == WarehouseTaskStatus.Cancelled) return;
        Status = WarehouseTaskStatus.Cancelled;
    }

    /// <summary>Начать на ТСД (повторный старт задания в работе ничего не меняет).</summary>
    public void StartWork(DateTimeOffset now)
    {
        if (!IsOpen) throw new DomainConflictException("Задание закрыто");
        if (Status == WarehouseTaskStatus.InProgress) return;
        Status = WarehouseTaskStatus.InProgress;
        StartedAt = now;
    }

    /// <summary>Текущая позиция маршрута — первая недособранная; null — всё собрано.</summary>
    public int? CurrentStep
    {
        get
        {
            var index = Items.FindIndex(i => !i.IsComplete);
            return index < 0 ? null : index;
        }
    }

    /// <summary>Засчитать одну единицу по текущей позиции.</summary>
    public TaskItem ScanUnit()
    {
        if (Status != WarehouseTaskStatus.InProgress) throw new DomainConflictException("Задание не в работе");
        if (CurrentStep is not { } index) throw new DomainConflictException("Все позиции уже собраны");
        Items[index].Tick();
        return Items[index];
    }

    /// <summary>Завершить на ТСД: все позиции должны быть выполнены (кроме инвентаризации).</summary>
    public void CompleteWork(DateTimeOffset now)
    {
        if (Status != WarehouseTaskStatus.InProgress) throw new DomainConflictException("Задание не в работе");
        if (Type != TaskType.Inventory && Items.Any(i => !i.IsComplete))
            throw new DomainValidationException("Не все позиции выполнены", "Досканируйте товары или сообщите бригадиру о недостаче.");
        Status = WarehouseTaskStatus.Done;
        FinishedAt = now;
    }

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
