using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>Договор селлера с фулфилмент-центром на набор складов.</summary>
public class Contract
{
    private Contract() { }

    public Contract(string id, string fulfillmentId, string sellerId, IEnumerable<string> warehouseIds, DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new DomainException("Не задан идентификатор договора");
        Id = id; FulfillmentId = fulfillmentId; SellerId = sellerId;
        WarehouseIds = warehouseIds.Distinct().ToList(); StartedAt = startedAt;
    }

    public string Id { get; private set; } = null!;
    public string FulfillmentId { get; private set; } = null!;
    public string SellerId { get; private set; } = null!;
    public List<string> WarehouseIds { get; private set; } = [];
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Номер договора для UI: «Д-0012» из цифр идентификатора.</summary>
    public string Number => $"Д-{new string(Id.Where(char.IsDigit).ToArray()).PadLeft(4, '0')}";
}
