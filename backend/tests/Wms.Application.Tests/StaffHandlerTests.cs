using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Staff;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class StaffHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db = new();
    private readonly IClock _clock = Substitute.For<IClock>();
    // Среда, 10:00 МСК.
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 7, 0, 0, TimeSpan.Zero);
    private static readonly RequestContext Owner = new("usr_o", "org_fc", Role.FcOwner, OrganizationStatus.Active, OrganizationType.Fulfillment);
    private static readonly Shift[] Week = [Shift.Day, Shift.Day, Shift.Day, Shift.Day, Shift.Day, Shift.Off, Shift.Off];

    public StaffHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        var org = new Organization("org_fc", OrganizationType.Fulfillment, "ФЦ", "1", "М", OrganizationStatus.Active, Now);
        _db.Organizations.Add(org);
        _db.Warehouses.AddRange(new Warehouse("wh_1", "org_fc", "Склад", "W1", "М", "ул.", 100, true), new Warehouse("wh_2", "org_fc", "Второй", "W2", "М", "ул.", 100, true),
            new Warehouse("wh_x", "org_other", "Чужой", "WX", "М", "ул.", 100, true));
        _db.Zones.AddRange(new Zone("z_1", "wh_1", "STG", "Хранение", ZoneType.Storage), new Zone("z_2", "wh_2", "STG", "Хранение", ZoneType.Storage));
        var owner = new User("usr_o", "o@x.ru", "h", "Борис", null, false);
        owner.JoinOrganization(org, Role.FcOwner);
        var pickerB = new User("usr_b", "b@x.ru", "h", "Яна", null, false);
        pickerB.JoinOrganization(org, Role.FcPicker);
        var pickerA = new User("usr_a", "a@x.ru", "h", "Антон", "+7", false);
        pickerA.JoinOrganization(org, Role.FcPicker);
        var noProfile = new User("usr_n", "n@x.ru", "h", "Без профиля", null, false);
        noProfile.JoinOrganization(org, Role.FcPacker);
        _db.Users.AddRange(owner, pickerB, pickerA, noProfile);
        _db.StaffProfiles.AddRange(
            new StaffProfile("usr_o", "wh_1", [], [], true, 0, 0, Now.AddYears(-1), Week),
            new StaffProfile("usr_b", "wh_2", ["z_2"], [Skill.Picking], true, 55, 98, Now, Week),
            new StaffProfile("usr_a", "wh_1", ["z_1"], [Skill.Picking], true, 70, 99, Now, [Shift.Day, Shift.Day, Shift.Night, Shift.Day, Shift.Day, Shift.Off, Shift.Off]));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private StaffHandler Handler => new(_db, _db, _clock);

    [Fact]
    public async Task Lists_profiles_sorted_with_on_shift_and_week_start()
    {
        var res = await Handler.ListAsync(Owner, null, default);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 21, 0, 0, TimeSpan.Zero), res.WeekStart); // пн 00:00 МСК
        Assert.Equal(["usr_o", "usr_a", "usr_b"], res.Items.Select(i => i.Id));
        var a = res.Items[1];
        Assert.False(a.OnShift); // среда — ночная смена, 10:00
        Assert.True(res.Items[2].OnShift);
        Assert.Equal(60, a.Norm);
        Assert.Equal("Склад", a.WarehouseName);
        Assert.Equal("+7", a.Phone);
        Assert.Equal(["usr_b"], (await Handler.ListAsync(Owner, "wh_2", default)).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Non_fulfillment_is_forbidden()
    {
        var seller = new RequestContext("usr_s", "org_s", Role.SellerOwner, OrganizationStatus.Active, OrganizationType.Seller);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppException>(() => Handler.ListAsync(seller, null, default))).Status);
    }

    [Fact]
    public async Task Update_validates_and_overrides_role()
    {
        var bad = await Assert.ThrowsAsync<AppException>(() => Handler.UpdateAsync(Owner, "usr_a", new EmployeeInput(Role.FcPacker, "wh_x", [], [], true), default));
        Assert.Equal("Выберите склад", bad.Title);
        var self = await Assert.ThrowsAsync<AppException>(() => Handler.UpdateAsync(Owner, "usr_o", new EmployeeInput(Role.FcOwner, "wh_1", [], [], false), default));
        Assert.Equal("Нельзя деактивировать себя", self.Title);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => Handler.UpdateAsync(Owner, "usr_n", new EmployeeInput(Role.FcPacker, "wh_1", [], [], true), default))).Status);

        var dto = await Handler.UpdateAsync(Owner, "usr_a", new EmployeeInput(Role.FcPacker, "wh_2", ["z_1", "z_2"], [Skill.Packing], true), default);
        Assert.Equal(Role.FcPacker, dto.Role);
        Assert.Equal(45, dto.Norm);
        Assert.Equal(["z_2"], dto.ZoneIds);
        Assert.Equal("Второй", dto.WarehouseName);
    }

    [Fact]
    public async Task Set_shift_validates_day()
    {
        var dto = await Handler.SetShiftAsync(Owner, "usr_a", 2, Shift.Day, default);
        Assert.True(dto.OnShift);
        Assert.Equal("Некорректный день", (await Assert.ThrowsAsync<AppException>(() => Handler.SetShiftAsync(Owner, "usr_a", 9, Shift.Day, default))).Title);
    }
}
