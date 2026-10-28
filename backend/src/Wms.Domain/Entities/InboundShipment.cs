using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Строка поставки.</summary>
public sealed class InboundLine
{
    private InboundLine() { }

    public InboundLine(string id, string productId, int expectedQty, int receivedQty = 0, int defectQty = 0,
        string? lotNumber = null, DateTimeOffset? expiresAt = null)
    {
        Id = id; ProductId = productId; ExpectedQty = expectedQty; ReceivedQty = receivedQty; DefectQty = defectQty;
        LotNumber = lotNumber; ExpiresAt = expiresAt;
    }

    public string Id { get; private set; } = null!;
    public string ProductId { get; private set; } = null!;
    public int ExpectedQty { get; private set; }
    public int ReceivedQty { get; private set; }
    public int DefectQty { get; private set; }
    public string? LotNumber { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Годные единицы к размещению.</summary>
    public int GoodQty => ReceivedQty - DefectQty;

    internal void Inspect(ReceivingResult result)
    {
        if (ReceivedQty == 0) ReceivedQty = result.Received;
        DefectQty = result.Defect;
    }
}

/// <summary>Событие истории поставки.</summary>
public sealed class InboundEvent
{
    private InboundEvent() { }

    public InboundEvent(string id, InboundEventType type, DateTimeOffset at, string title, string actorName, string? description = null)
    {
        Id = id; Type = type; At = at; Title = title; ActorName = actorName; Description = description;
    }

    public string Id { get; private set; } = null!;
    public InboundEventType Type { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string Title { get; private set; } = null!;
    public string ActorName { get; private set; } = null!;
    public string? Description { get; private set; }
}

/// <summary>Результат пересчёта строки при завершении приёмки.</summary>
public readonly record struct ReceivingResult(int Received, int Defect);

/// <summary>Порт пересчёта: фактически принятое и брак по строке (в моках — случайные расхождения).</summary>
public interface IReceivingInspector
{
    ReceivingResult Inspect(InboundLine line);
}

/// <summary>Поставка селлера на склад ФЦ с машиной состояний приёмки.</summary>
public sealed class InboundShipment
{
    private sealed record Transition(InboundStatus[] From, InboundStatus To, string Title);

    private static readonly Dictionary<InboundAction, Transition> Transitions = new()
    {
        [InboundAction.Arrive] = new([InboundStatus.InTransit], InboundStatus.Arrived, "Машина прибыла на склад"),
        [InboundAction.StartReceiving] = new([InboundStatus.Arrived], InboundStatus.Receiving, "Начата приёмка"),
        [InboundAction.CompleteReceiving] = new([InboundStatus.Receiving], InboundStatus.Received, "Приёмка завершена"),
        [InboundAction.CompletePlacement] = new([InboundStatus.Received], InboundStatus.Placed, "Товар размещён в ячейках"),
        [InboundAction.Cancel] = new([InboundStatus.Draft, InboundStatus.InTransit], InboundStatus.Cancelled, "Поставка отменена"),
    };

    private InboundShipment() { }

    public InboundShipment(string id, string number, InboundStatus status, string sellerId, string warehouseId,
        DateTimeOffset createdAt, DateTimeOffset expectedAt, PackageType packageType, int packagesCount,
        string? comment, string? driverName, string? vehicleNumber, IEnumerable<InboundLine> lines, IEnumerable<InboundEvent> events)
    {
        Id = id; Number = number; Status = status; SellerId = sellerId; WarehouseId = warehouseId;
        CreatedAt = createdAt; ExpectedAt = expectedAt; PackageType = packageType; PackagesCount = packagesCount;
        Comment = comment; DriverName = driverName; VehicleNumber = vehicleNumber;
        Lines = lines.ToList(); Events = events.ToList();
    }

    public string Id { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public InboundStatus Status { get; private set; }
    public string SellerId { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpectedAt { get; private set; }
    public PackageType PackageType { get; private set; }
    public int PackagesCount { get; private set; }
    public string? Comment { get; private set; }
    public string? DriverName { get; private set; }
    public string? VehicleNumber { get; private set; }
    public List<InboundLine> Lines { get; private set; } = [];
    public List<InboundEvent> Events { get; private set; } = [];

    public int ExpectedUnits => Lines.Sum(l => l.ExpectedQty);
    public int ReceivedUnits => Lines.Sum(l => l.ReceivedQty);
    public int DefectUnits => Lines.Sum(l => l.DefectQty);
    public int ShortageUnits => Lines.Sum(l => Math.Max(0, l.ExpectedQty - l.ReceivedQty));

    /// <summary>Расхождение фиксируется только после приёмки.</summary>
    public bool HasDiscrepancy => Status is InboundStatus.Received or InboundStatus.Placed &&
        Lines.Any(l => l.ReceivedQty != l.ExpectedQty || l.DefectQty > 0);

    /// <summary>Активная поставка (в пути или на приёмке) — для карточки клиента.</summary>
    public bool IsActive => Status is InboundStatus.InTransit or InboundStatus.Arrived or InboundStatus.Receiving;

    /// <summary>Отменить может и селлер; остальные действия — только офис ФЦ.</summary>
    public static bool IsSellerAction(InboundAction action) => action == InboundAction.Cancel;

    public bool CanApply(InboundAction action) => Transitions[action].From.Contains(Status);

    /// <summary>Переход по действию с записью событий; для завершения приёмки нужен инспектор.</summary>
    public void Apply(InboundAction action, string actorName, DateTimeOffset now, IReceivingInspector? inspector = null)
    {
        if (!CanApply(action)) throw new DomainConflictException("Действие недоступно");
        var transition = Transitions[action];
        Status = transition.To;
        var stamp = Base36(now.ToUnixTimeMilliseconds());
        void Push(InboundEventType type, string title, string? description = null) =>
            Events.Add(new InboundEvent($"{Id}_e{Events.Count + 1}_{stamp}", type, now, title, actorName, description));

        switch (action)
        {
            case InboundAction.CompleteReceiving:
                ArgumentNullException.ThrowIfNull(inspector);
                foreach (var line in Lines) line.Inspect(inspector.Inspect(line));
                Push(InboundEventType.Received, transition.Title, $"Принято {ReceivedUnits} из {ExpectedUnits} шт");
                var parts = new List<string>();
                if (ShortageUnits > 0) parts.Add($"недостача {ShortageUnits} шт");
                if (DefectUnits > 0) parts.Add($"брак {DefectUnits} шт");
                if (parts.Count > 0) Push(InboundEventType.Discrepancy, "Обнаружены расхождения", string.Join(", ", parts));
                break;
            case InboundAction.CompletePlacement:
                Push(InboundEventType.Placed, transition.Title, $"Размещено {Lines.Count} позиций");
                break;
            default:
                Push(Enum.Parse<InboundEventType>(transition.To.ToString()), transition.Title);
                break;
        }
    }

    private static string Base36(long value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (value <= 0) return "0";
        var chars = new Stack<char>();
        while (value > 0) { chars.Push(digits[(int)(value % 36)]); value /= 36; }
        return new string([.. chars]);
    }
}
