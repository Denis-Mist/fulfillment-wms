using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Строка заказа: товар, количество и цена продажи.</summary>
public sealed class OrderLine
{
    private OrderLine() { }

    public OrderLine(string productId, int quantity, double price)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        ProductId = productId; Quantity = quantity; Price = price;
    }

    public string ProductId { get; private set; } = null!;
    public int Quantity { get; private set; }
    public double Price { get; private set; }
}

/// <summary>Заказ покупателя маркетплейса/сайта, исполняемый складом ФЦ.</summary>
public class Order
{
    private static readonly OrderStatus[] ActiveStatuses =
        [OrderStatus.New, OrderStatus.Reserved, OrderStatus.OutOfStock, OrderStatus.Picking, OrderStatus.Picked, OrderStatus.Packed];

    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public Order(string id, string number, string? externalNumber, SalesChannel channel, OrderStatus status, string sellerId,
        string warehouseId, DateTimeOffset createdAt, DateTimeOffset shipBy, DateTimeOffset? shippedAt, string deliveryCity,
        IEnumerable<OrderLine> lines)
    {
        Id = id; Number = number; ExternalNumber = externalNumber; Channel = channel; Status = status; SellerId = sellerId;
        WarehouseId = warehouseId; CreatedAt = createdAt; ShipBy = shipBy; ShippedAt = shippedAt; DeliveryCity = deliveryCity;
        _lines.AddRange(lines);
        if (_lines.Count == 0) throw new ArgumentException("Заказ без строк", nameof(lines));
    }

    public string Id { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public string? ExternalNumber { get; private set; }
    public SalesChannel Channel { get; private set; }
    public OrderStatus Status { get; private set; }
    public string SellerId { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ShipBy { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public string DeliveryCity { get; private set; } = null!;
    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>Заказ ещё в работе склада (isOrderActive в моках).</summary>
    public bool IsActive => IsActiveStatus(Status);
    public int Units => _lines.Sum(l => l.Quantity);
    public double Amount => _lines.Sum(l => l.Quantity * l.Price);
    /// <summary>Не отменён — учитывается в продажах.</summary>
    public bool CountsAsSale => Status != OrderStatus.Cancelled;
    public bool ShippedOnTime => ShippedAt is { } at && at <= ShipBy;

    public static bool IsActiveStatus(OrderStatus status) => ActiveStatuses.Contains(status);

    public bool IsOverdue(DateTimeOffset now) => IsActive && ShipBy < now;

    /// <summary>Количество товара в заказе.</summary>
    public int QuantityOf(string productId) => _lines.Where(l => l.ProductId == productId).Sum(l => l.Quantity);

    /// <summary>Сдвиг всех дат (сид — снимок, перенесённый к текущему времени).</summary>
    public void ShiftTimes(TimeSpan delta)
    {
        CreatedAt += delta; ShipBy += delta;
        if (ShippedAt is { } at) ShippedAt = at + delta;
    }
}
