using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class PlatformEntitiesTests
{
    private static Organization Org(OrganizationStatus status) =>
        new("org_x", OrganizationType.Seller, "X", "7700000000", "Москва", status, DateTimeOffset.UnixEpoch, null);

    [Theory]
    [InlineData(OrganizationStatus.Pending, ModerationAction.Approve, OrganizationStatus.Active)]
    [InlineData(OrganizationStatus.Rejected, ModerationAction.Approve, OrganizationStatus.Active)]
    [InlineData(OrganizationStatus.Pending, ModerationAction.Reject, OrganizationStatus.Rejected)]
    [InlineData(OrganizationStatus.Active, ModerationAction.Block, OrganizationStatus.Blocked)]
    [InlineData(OrganizationStatus.Blocked, ModerationAction.Unblock, OrganizationStatus.Active)]
    public void Moderation_follows_mock_transitions(OrganizationStatus from, ModerationAction action, OrganizationStatus to)
    {
        var org = Org(from);
        org.Moderate(action);
        Assert.Equal(to, org.Status);
    }

    [Theory]
    [InlineData(OrganizationStatus.Active, ModerationAction.Approve)]
    [InlineData(OrganizationStatus.Active, ModerationAction.Reject)]
    [InlineData(OrganizationStatus.Pending, ModerationAction.Block)]
    [InlineData(OrganizationStatus.Active, ModerationAction.Unblock)]
    public void Moderation_rejects_invalid_transition(OrganizationStatus from, ModerationAction action)
    {
        var e = Assert.Throws<DomainConflictException>(() => Org(from).Moderate(action));
        Assert.Equal("Действие недоступно для текущего статуса", e.Message);
    }

    [Fact]
    public void Platform_admin_cannot_be_blocked()
    {
        var admin = new User("u", "a@b.ru", "h", "A", null, isPlatformAdmin: true);
        Assert.Throws<DomainConflictException>(admin.Block);
        var user = new User("u2", "b@b.ru", "h", "B", null, isPlatformAdmin: false);
        user.Block();
        Assert.True(user.IsBlocked);
        user.Unblock();
        Assert.False(user.IsBlocked);
    }

    [Theory]
    [InlineData(" pcs ", "PCS")]
    [InlineData("box_2", "BOX_2")]
    public void Reference_code_is_normalized(string code, string expected) =>
        Assert.Equal(expected, ReferenceItem.Create("id", ReferenceKind.Units, code, " Штука ", "").Code);

    [Theory]
    [InlineData("x", "Штука")]
    [InlineData("ТОВАР", "Штука")]
    [InlineData("PCS", " ")]
    public void Reference_rejects_bad_input(string code, string name) =>
        Assert.Throws<DomainException>(() => ReferenceItem.Create("id", ReferenceKind.Units, code, name, null));

    [Fact]
    public void Client_request_can_be_decided_once()
    {
        var r = new ClientRequest("org_fc1", "creq_1", "C", "1", "М", "A", "+7", 1, 1, ["wb"], "", DateTimeOffset.UnixEpoch, null);
        r.Decline(DateTimeOffset.UnixEpoch.AddHours(1));
        Assert.Equal(ClientRequestStatus.Declined, r.Status);
        Assert.Throws<DomainConflictException>(() => r.Accept(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Contract_number_pads_digits_of_id() =>
        Assert.Equal("Д-0011", new Contract("ctr_s1_fc1", "org_fc1", "org_s1", ["w"], DateTimeOffset.UnixEpoch).Number);

    [Fact]
    public void Profile_update_keeps_inn()
    {
        var p = new OrgProfile("A", "7700000000", "", "", "", "", "", "", "", "", "");
        var s = new OrganizationSettings("org", p, CardRules.Default, NotificationPrefs.DefaultFor(OrganizationType.Seller));
        s.UpdateProfile(p with { Name = "B", Inn = "1" });
        Assert.Equal(("B", "7700000000"), (s.Profile.Name, s.Profile.Inn));
    }
}
