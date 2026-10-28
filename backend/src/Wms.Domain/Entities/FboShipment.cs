using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class FboLine
{
    private FboLine() { }

    public FboLine(string productId, int quantity, int packed = 0)
    {
        if (quantity <= 0) throw new DomainValidationException("Количество должно быть больше нуля");
        ProductId = productId;
        Quantity = quantity;
        Packed = Math.Clamp(packed, 0, quantity);
    }

    public string ProductId { get; private set; } = null!;
    public int Quantity { get; private set; }
    public int Packed { get; internal set; }
}

public sealed class FboHistoryEntry
{
    private FboHistoryEntry() { }

    public FboHistoryEntry(DateTimeOffset at, FboStatus status, string by)
    {
        At = at;
        Status = status;
        By = by;
    }

    public DateTimeOffset At { get; internal set; }
    public FboStatus Status { get; private set; }
    public string By { get; private set; } = null!;
}

/// <summary>Отгрузка FBO на склад маркетплейса: draft → confirmed → assembling → ready → shipped (или cancelled до сборки).</summary>
public sealed class FboShipment
{
    /// <summary>Склады маркетплейсов (FBO_DESTINATIONS во фронте).</summary>
    public static readonly IReadOnlyDictionary<FboMarketplace, string[]> Destinations = new Dictionary<FboMarketplace, string[]>
    {
        [FboMarketplace.Wb] = ["WB Коледино", "WB Электросталь", "WB Подольск", "WB Казань", "WB Краснодар"],
        [FboMarketplace.Ozon] = ["Ozon Хоругвино", "Ozon Пушкино", "Ozon Тверь", "Ozon Новая Рига"],
    };

    /// <summary>Следующий шаг для сотрудника ФЦ (FBO_NEXT).</summary>
    public static FboStatus? NextFor(FboStatus status) => status switch
    {
        FboStatus.Confirmed => FboStatus.Assembling,
        FboStatus.Assembling => FboStatus.Ready,
        FboStatus.Ready => FboStatus.Shipped,
        _ => null,
    };

    public static bool IsActiveStatus(FboStatus s) => s is FboStatus.Confirmed or FboStatus.Assembling or FboStatus.Ready;

    private readonly List<FboLine> _lines = [];
    private readonly List<FboHistoryEntry> _history = [];

    private FboShipment() { }

    public FboShipment(string id, string number, FboMarketplace marketplace, string destination, FboStatus status, string sellerId,
        string warehouseId, DateTimeOffset plannedAt, DateTimeOffset createdAt, string? supplyId, IEnumerable<FboLine> lines,
        IEnumerable<FboHistoryEntry> history)
    {
        Id = id; Number = number; Marketplace = marketplace; Destination = destination; Status = status; SellerId = sellerId;
        WarehouseId = warehouseId; PlannedAt = plannedAt; CreatedAt = createdAt; SupplyId = supplyId;
        _lines.AddRange(lines);
        _history.AddRange(history);
        if (_lines.Count == 0) throw new DomainValidationException("Добавьте товары в заявку");
    }

    /// <summary>Новая заявка селлера в статусе draft.</summary>
    public static FboShipment Create(string id, string number, FboMarketplace marketplace, string destination, string sellerId,
        string warehouseId, DateTimeOffset plannedAt, IEnumerable<FboLine> lines, string by, DateTimeOffset now)
    {
        if (!Destinations.TryGetValue(marketplace, out var list) || !list.Contains(destination))
            throw new DomainValidationException("Выберите склад маркетплейса");
        return new FboShipment(id, number, marketplace, destination, FboStatus.Draft, sellerId, warehouseId, plannedAt, now, null, lines,
            [new FboHistoryEntry(now, FboStatus.Draft, by)]);
    }

    public string Id { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public FboMarketplace Marketplace { get; private set; }
    public string Destination { get; private set; } = null!;
    public FboStatus Status { get; private set; }
    public string SellerId { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public DateTimeOffset PlannedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? SupplyId { get; private set; }
    public IReadOnlyList<FboLine> Lines => _lines;
    public IReadOnlyList<FboHistoryEntry> History => _history;

    public int Units => _lines.Sum(l => l.Quantity);
    public int Packed => _lines.Sum(l => l.Packed);
    /// <summary>Короба по 40 шт., минимум один.</summary>
    public int Boxes => Math.Max(1, (int)Math.Ceiling(Units / 40.0));

    public void Submit(string supplyNumber, string by, DateTimeOffset now)
    {
        if (Status != FboStatus.Draft) throw new DomainConflictException("Заявка уже отправлена");
        SupplyId = (Marketplace == FboMarketplace.Wb ? "WB-GI-" : "OZ-") + supplyNumber;
        Step(FboStatus.Confirmed, by, now);
    }

    public void Cancel(string by, DateTimeOffset now)
    {
        if (Status is not (FboStatus.Draft or FboStatus.Confirmed)) throw new DomainConflictException("Отменить можно до начала сборки");
        Step(FboStatus.Cancelled, by, now);
    }

    public void Advance(FboStatus to, string by, DateTimeOffset now)
    {
        if (NextFor(Status) != to) throw new DomainConflictException("Недопустимый переход статуса");
        Step(to, by, now);
        if (to == FboStatus.Ready) foreach (var l in _lines) l.Packed = l.Quantity;
        if (to == FboStatus.Assembling) foreach (var l in _lines) l.Packed = 0;
    }

    public void ShiftTimes(TimeSpan delta)
    {
        PlannedAt += delta;
        CreatedAt += delta;
        foreach (var h in _history) h.At += delta;
    }

    private void Step(FboStatus status, string by, DateTimeOffset now)
    {
        Status = status;
        _history.Add(new FboHistoryEntry(now, status, by));
    }
}
