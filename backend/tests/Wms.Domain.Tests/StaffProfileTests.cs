using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class StaffProfileTests
{
    private static StaffProfile Profile(bool active = true) => new("usr_1", "wh_1", ["z_1"], [Skill.Picking], active, 50, 99.1,
        DateTimeOffset.UnixEpoch, [Shift.Day, Shift.Night, Shift.Off, Shift.Vacation, Shift.Day, Shift.Off, Shift.Off]);

    [Theory]
    [InlineData(0, 8, true)]
    [InlineData(0, 20, false)]
    [InlineData(1, 23, true)]
    [InlineData(1, 7, true)]
    [InlineData(1, 12, false)]
    [InlineData(2, 12, false)]
    [InlineData(3, 12, false)]
    public void On_shift_by_schedule(int day, int hour, bool expected) => Assert.Equal(expected, Profile().IsOnShift(day, hour));

    [Fact]
    public void Inactive_is_never_on_shift() => Assert.False(Profile(active: false).IsOnShift(0, 10));

    [Fact]
    public void Norms_and_role_override()
    {
        Assert.Equal(90, StaffProfile.NormFor(Role.FcReceiver));
        Assert.Equal(60, StaffProfile.NormFor(Role.FcPicker));
        Assert.Equal(45, StaffProfile.NormFor(Role.FcPacker));
        Assert.Equal(0, StaffProfile.NormFor(Role.FcManager));
        var p = Profile();
        Assert.Equal(Role.FcPicker, p.EffectiveRole(Role.FcPicker));
        p.Update(Role.FcPacker, "wh_2", ["z_a", "z_foreign"], new HashSet<string> { "z_a" }, [Skill.Packing], false);
        Assert.Equal(Role.FcPacker, p.EffectiveRole(Role.FcPicker));
        Assert.Equal(["z_a"], p.ZoneIds);
        Assert.Equal("wh_2", p.WarehouseId);
        Assert.False(p.Active);
    }

    [Fact]
    public void Set_shift_validates_day()
    {
        var p = Profile();
        p.SetShift(2, Shift.Night);
        Assert.Equal(Shift.Night, p.Shifts[2]);
        Assert.Throws<DomainValidationException>(() => p.SetShift(7, Shift.Day));
        Assert.Throws<DomainValidationException>(() => p.SetShift(-1, Shift.Day));
    }

    [Fact]
    public void Schedule_must_have_seven_days() =>
        Assert.Throws<DomainException>(() => new StaffProfile("u", "w", [], [], true, 0, 0, DateTimeOffset.UnixEpoch, [Shift.Day]));
}
