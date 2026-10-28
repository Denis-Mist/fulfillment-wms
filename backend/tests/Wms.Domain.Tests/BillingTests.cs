using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class BillingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Act_pays_once_and_due_is_ten_days()
    {
        var act = new BillingAct("act_1", "ctr_1", "2026-09", 100m, ActStatus.Issued, Now, null);
        Assert.Equal(Now.AddDays(10), act.DueAt);
        act.Pay(Now);
        Assert.Equal(ActStatus.Paid, act.Status);
        Assert.Throws<DomainConflictException>(() => act.Pay(Now));
        Assert.StartsWith("АКТ-202609-", BillingAct.NumberFor("ctr_1", "2026-09"));
    }

    [Fact]
    public void Tariff_rejects_negative_rates()
    {
        var t = Tariff.Default("ctr_1");
        Assert.True(t.PickPerOrder > 0);
        Assert.Throws<DomainValidationException>(() => t.Update(-1, 1, 1, 1, 1, 1));
        t.Update(1, 2, 3, 4, 5, 6);
        Assert.Equal(3, t.RateFor(BillingService.Picking));
    }

    [Fact]
    public void Integration_connect_sync_and_settings()
    {
        var i = new MarketplaceIntegration("org", IntegrationChannel.Ozon);
        Assert.Throws<DomainConflictException>(() => i.Sync(1, 1, Now));
        Assert.Throws<DomainValidationException>(() => i.Connect("short", "1234", Now));
        Assert.Throws<DomainValidationException>(() => i.Connect(new string('k', 24), "abc", Now));
        i.Connect(new string('k', 20) + "WXYZ", "123456", Now);
        Assert.Equal("••••WXYZ", i.KeyMask);
        Assert.Throws<DomainValidationException>(() => i.UpdateSettings(true, true, 7));
        i.UpdateSettings(true, true, 30);
        for (var n = 0; n < 20; n++) i.Sync(2, 70, Now);
        Assert.Equal(MarketplaceIntegration.LogLimit, i.Log.Count);
        Assert.Equal(IntegrationLogKind.Stock, i.Log[0].Kind);
        i.Disconnect();
        Assert.Equal(IntegrationStatus.Disconnected, i.Status);
    }
}
