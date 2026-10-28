using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class AnalyticsRecordsTests
{
    [Fact]
    public void Plan_key_and_reapproval()
    {
        Assert.Equal("org:all", StaffPlan.KeyFor("org", null));
        var plan = new StaffPlan("org:all", ForecastAlgorithm.HoltWinters, "A", DateTimeOffset.UnixEpoch);
        plan.Approve(ForecastAlgorithm.MovingAverage, "B", DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.Equal(ForecastAlgorithm.MovingAverage, plan.Algorithm);
        Assert.Equal("B", plan.ApprovedBy);
    }

    [Fact]
    public void Slotting_requires_id() =>
        Assert.Throws<DomainValidationException>(() => new SlottingApplied(" ", DateTimeOffset.UnixEpoch));
}
