using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Остаток товара в ячейке.</summary>
public class StockItem
{
    private StockItem() { }

    public StockItem(string id, string productId, string sellerId, string warehouseId, string locationId, int quantity, int reserved)
    {
        Id = id; ProductId = productId; SellerId = sellerId; WarehouseId = warehouseId; LocationId = locationId;
        Quantity = quantity; Reserved = reserved;
    }

    public string Id { get; private set; } = null!;
    public string ProductId { get; private set; } = null!;
    public string SellerId { get; private set; } = null!;
    public string WarehouseId { get; private set; } = null!;
    public string LocationId { get; private set; } = null!;
    public int Quantity { get; private set; }
    public int Reserved { get; private set; }

    /// <summary>Свободный (незарезервированный) остаток.</summary>
    public int Free => Quantity - Reserved;

    /// <summary>Пустая позиция без резерва — удаляется после перемещения.</summary>
    public bool IsEmpty => Quantity == 0 && Reserved == 0;

    /// <summary>Статус как в моках: ячейка брака → defect; весь остаток в резерве → reserved.</summary>
    public StockStatus StatusIn(ZoneType? zone) =>
        zone == ZoneType.Defect ? StockStatus.Defect : Reserved > 0 && Reserved >= Quantity ? StockStatus.Reserved : StockStatus.Available;

    /// <summary>Доступно к отгрузке: в зоне брака 0, иначе свободный остаток (не меньше 0).</summary>
    public int AvailableIn(ZoneType? zone) => zone == ZoneType.Defect ? 0 : Math.Max(0, Free);

    /// <summary>Расход свободного остатка (перемещение/списание); резерв трогать нельзя.</summary>
    public void Take(int quantity)
    {
        if (quantity <= 0) throw new Common.DomainException("Количество должно быть больше нуля");
        if (quantity > Free) throw new Common.DomainConflictException($"Свободно {Free} шт");
        Quantity -= quantity;
    }

    /// <summary>Инвентаризация: факт не может быть меньше резерва. Возвращает расхождение (факт − учёт).</summary>
    public int Recount(int counted)
    {
        if (counted < 0) throw new Common.DomainException("Фактическое количество не может быть отрицательным");
        if (counted < Reserved) throw new Common.DomainConflictException($"Под заказы зарезервировано {Reserved} шт");
        var delta = counted - Quantity;
        Quantity = counted;
        return delta;
    }

    /// <summary>Приход годного товара на остаток.</summary>
    public void Receive(int quantity)
    {
        if (quantity <= 0) throw new Common.DomainException("Количество должно быть больше нуля");
        Quantity += quantity;
    }
}
