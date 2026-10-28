using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReturnLine
{
    private ReturnLine() { }

    public ReturnLine(string productId, int quantity, ReturnGrade? grade = null)
    {
        ProductId = productId;
        Quantity = quantity;
        Grade = grade;
    }

    public string ProductId { get; private set; } = null!;
    public int Quantity { get; private set; }
    public ReturnGrade? Grade { get; internal set; }

    /// <summary>Годен к возврату в остаток (RETURN_GRADES[grade].restock).</summary>
    public bool Restock => Grade is ReturnGrade.AsNew or ReturnGrade.Resale;
}

/// <summary>Возврат покупателя: expected → received → inspected → closed.</summary>
public sealed class CustomerReturn
{
    private readonly List<ReturnLine> _lines = [];

    private CustomerReturn() { }

    public CustomerReturn(string id, string number, string orderId, ReturnReason reason, ReturnStatus status, string sellerId, string warehouseId,
        DateTimeOffset createdAt, DateTimeOffset? receivedAt, DateTimeOffset? inspectedAt, string? inspectedBy, string? comment, int photos,
        IEnumerable<ReturnLine> lines)
    {
        Id = id; Number = number; OrderId = orderId; Reason = reason; Status = status; SellerId = sellerId; WarehouseId = warehouseId;
        CreatedAt = createdAt; ReceivedAt = receivedAt; InspectedAt = inspectedAt; InspectedBy = inspectedBy; Comment = comment; Photos = photos;
        _lines.AddRange(lines);
        if (_lines.Count == 0) throw new DomainException("Возврат без строк");
    }

    public string Id { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public string OrderId { get; private set; } = null!;
    public ReturnReason Reason { get; private set; }
    public ReturnStatus Status { get; private set; }
    public string SellerId { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public DateTimeOffset? InspectedAt { get; private set; }
    public string? InspectedBy { get; private set; }
    public string? Comment { get; private set; }
    public int Photos { get; private set; }
    public IReadOnlyList<ReturnLine> Lines => _lines;

    public int Units => _lines.Sum(l => l.Quantity);

    /// <summary>Итог оценки: всё в остаток, всё в брак или смешанно; до оценки — null.</summary>
    public ReturnResolution? Resolution
    {
        get
        {
            if (Status is not (ReturnStatus.Inspected or ReturnStatus.Closed)) return null;
            var restock = _lines.Count(l => l.Restock);
            return restock == _lines.Count ? ReturnResolution.Restocked : restock == 0 ? ReturnResolution.Defect : ReturnResolution.Mixed;
        }
    }

    public void Receive(DateTimeOffset now)
    {
        if (Status != ReturnStatus.Expected) throw new DomainConflictException("Возврат уже принят");
        Status = ReturnStatus.Received;
        ReceivedAt = now;
    }

    public void Inspect(IReadOnlyDictionary<string, ReturnGrade> grades, string? comment, string by, DateTimeOffset now)
    {
        if (Status != ReturnStatus.Received) throw new DomainConflictException("Оценка доступна после приёмки");
        if (_lines.Any(l => !grades.ContainsKey(l.ProductId)))
            throw new DomainValidationException("Оцените все позиции", "Для каждой позиции нужно указать состояние товара.");
        foreach (var l in _lines) l.Grade = grades[l.ProductId];
        Status = ReturnStatus.Inspected;
        InspectedAt = now;
        InspectedBy = by;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Photos = Math.Max(Photos, 1);
    }

    /// <summary>Закрытие; возвращает строки, годные к возврату в остаток.</summary>
    public IReadOnlyList<ReturnLine> Close()
    {
        if (Status != ReturnStatus.Inspected) throw new DomainConflictException("Закрыть можно только оценённый возврат");
        Status = ReturnStatus.Closed;
        return _lines.Where(l => l.Restock).ToList();
    }

    public void ShiftTimes(TimeSpan delta)
    {
        CreatedAt += delta;
        ReceivedAt += delta;
        InspectedAt += delta;
    }
}
