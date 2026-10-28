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
}
