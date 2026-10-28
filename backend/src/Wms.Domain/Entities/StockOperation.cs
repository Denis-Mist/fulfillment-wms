using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Запись журнала складских операций (перемещение, инвентаризация, списание). Названия — снимок на момент операции, как в моках.</summary>
public class StockOperation
{
    private StockOperation() { }

    public StockOperation(string id, string number, OperationType type, DateTimeOffset at, string actorName, string warehouseId,
        string warehouseName, string productId, string sku, string productName, string sellerName, int quantity,
        string? fromLocationCode = null, string? toLocationCode = null, int? expected = null, int? counted = null,
        ErrorReason? reason = null, string? comment = null)
    {
        Id = id; Number = number; Type = type; At = at; ActorName = actorName; WarehouseId = warehouseId; WarehouseName = warehouseName;
        ProductId = productId; Sku = sku; ProductName = productName; SellerName = sellerName; Quantity = quantity;
        FromLocationCode = fromLocationCode; ToLocationCode = toLocationCode; Expected = expected; Counted = counted; Reason = reason; Comment = comment;
    }

    public string Id { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public OperationType Type { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string ActorName { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public string WarehouseName { get; private set; } = null!;
    public string ProductId { get; private set; } = null!;
    public string Sku { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public string SellerName { get; private set; } = null!;
    public string? FromLocationCode { get; private set; }
    public string? ToLocationCode { get; private set; }
    public int Quantity { get; private set; }
    public int? Expected { get; private set; }
    public int? Counted { get; private set; }
    public ErrorReason? Reason { get; private set; }
    public string? Comment { get; private set; }

    /// <summary>Номер пользовательской операции: OP-(20000 + число операций в журнале).</summary>
    public static string NumberFor(long count) => $"OP-{20000 + count}";

    /// <summary>Причина расхождения при инвентаризации: недостача / пересорт / нет.</summary>
    public static ErrorReason? InventoryReason(int delta) => delta < 0 ? ErrorReason.Shortage : delta > 0 ? ErrorReason.Missort : null;
}
