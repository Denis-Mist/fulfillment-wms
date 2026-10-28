using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Заявка на подключение к фулфилмент-центру (ключ — ФЦ + идентификатор заявки).</summary>
public class ClientRequest
{
    private ClientRequest() { }

    public ClientRequest(string fulfillmentId, string id, string companyName, string inn, string city, string contactName,
        string contactPhone, int skuEstimate, int ordersPerDay, IEnumerable<string> marketplaces, string message,
        DateTimeOffset createdAt, string? sellerId)
    {
        FulfillmentId = fulfillmentId; Id = id; CompanyName = companyName; Inn = inn; City = city;
        ContactName = contactName; ContactPhone = contactPhone; SkuEstimate = skuEstimate; OrdersPerDay = ordersPerDay;
        Marketplaces = marketplaces.ToList(); Message = message; CreatedAt = createdAt; SellerId = sellerId;
        Status = ClientRequestStatus.Pending;
    }

    public string FulfillmentId { get; private set; } = null!;
    public string Id { get; private set; } = null!;
    public string CompanyName { get; private set; } = null!;
    public string Inn { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string ContactName { get; private set; } = null!;
    public string ContactPhone { get; private set; } = null!;
    public int SkuEstimate { get; private set; }
    public int OrdersPerDay { get; private set; }
    public List<string> Marketplaces { get; private set; } = [];
    public string Message { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public ClientRequestStatus Status { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? SellerId { get; private set; }

    public void Accept(DateTimeOffset now) => Decide(ClientRequestStatus.Accepted, now);
    public void Decline(DateTimeOffset now) => Decide(ClientRequestStatus.Declined, now);

    private void Decide(ClientRequestStatus status, DateTimeOffset now)
    {
        if (Status != ClientRequestStatus.Pending) throw new DomainConflictException("Заявка уже рассмотрена");
        Status = status;
        DecidedAt = now;
    }
}
