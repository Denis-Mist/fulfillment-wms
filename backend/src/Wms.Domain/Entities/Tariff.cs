using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>Тариф договора: ставки за услуги фулфилмента, ₽.</summary>
public sealed class Tariff
{
    private Tariff() { }

    public Tariff(string contractId, decimal receivingPerUnit, decimal storagePerM3Day, decimal pickPerOrder, decimal packPerUnit, decimal fboPerBox, decimal returnPerUnit)
    {
        ContractId = contractId;
        Update(receivingPerUnit, storagePerM3Day, pickPerOrder, packPerUnit, fboPerBox, returnPerUnit);
    }

    public string ContractId { get; private set; } = null!;
    public decimal ReceivingPerUnit { get; private set; }
    public decimal StoragePerM3Day { get; private set; }
    public decimal PickPerOrder { get; private set; }
    public decimal PackPerUnit { get; private set; }
    public decimal FboPerBox { get; private set; }
    public decimal ReturnPerUnit { get; private set; }

    /// <summary>Тариф по умолчанию, детерминированный от идентификатора договора (defaultTariff() из billing.ts).</summary>
    public static Tariff Default(string contractId)
    {
        var seed = contractId.Sum(ch => (int)ch);
        return new Tariff(contractId, 6 + seed % 3, 35 + seed % 5 * 5, 45 + seed % 4 * 5, 8 + seed % 3 * 2, 120 + seed % 3 * 20, 35 + seed % 2 * 10);
    }

    public void Update(decimal receivingPerUnit, decimal storagePerM3Day, decimal pickPerOrder, decimal packPerUnit, decimal fboPerBox, decimal returnPerUnit)
    {
        Check(receivingPerUnit, "receivingPerUnit"); Check(storagePerM3Day, "storagePerM3Day"); Check(pickPerOrder, "pickPerOrder");
        Check(packPerUnit, "packPerUnit"); Check(fboPerBox, "fboPerBox"); Check(returnPerUnit, "returnPerUnit");
        ReceivingPerUnit = receivingPerUnit; StoragePerM3Day = storagePerM3Day; PickPerOrder = pickPerOrder;
        PackPerUnit = packPerUnit; FboPerBox = fboPerBox; ReturnPerUnit = returnPerUnit;
    }

    public decimal RateFor(Enums.BillingService service) => service switch
    {
        Enums.BillingService.Receiving => ReceivingPerUnit,
        Enums.BillingService.Storage => StoragePerM3Day,
        Enums.BillingService.Picking => PickPerOrder,
        Enums.BillingService.Packing => PackPerUnit,
        Enums.BillingService.Fbo => FboPerBox,
        _ => ReturnPerUnit,
    };

    private static void Check(decimal value, string field)
    {
        if (value < 0) throw new DomainValidationException("Некорректный тариф", field);
    }
}
