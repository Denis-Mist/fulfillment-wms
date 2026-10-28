using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>Акт оказанных услуг за месяц (period = yyyy-MM): выставлен → оплачен.</summary>
public sealed class BillingAct
{
    private BillingAct() { }

    public BillingAct(string id, string contractId, string period, decimal amount, ActStatus status, DateTimeOffset issuedAt, DateTimeOffset? paidAt)
    {
        if (status == ActStatus.Draft) throw new DomainException("Черновик акта не хранится");
        Id = id; ContractId = contractId; Period = period; Amount = amount; Status = status; IssuedAt = issuedAt; PaidAt = paidAt;
    }

    public string Id { get; private set; } = null!;
    public string ContractId { get; private set; } = null!;
    public string Period { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public ActStatus Status { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset DueAt => IssuedAt.AddDays(10);

    public static string NumberFor(string contractId, string period) =>
        $"АКТ-{period.Replace("-", string.Empty)}-{new string(contractId.Where(char.IsDigit).ToArray()).PadLeft(3, '0')}";

    public void Pay(DateTimeOffset now)
    {
        if (Status != ActStatus.Issued) throw new DomainConflictException("Акт уже оплачен");
        Status = ActStatus.Paid;
        PaidAt = now;
    }
}
