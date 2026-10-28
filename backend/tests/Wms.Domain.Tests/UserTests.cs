using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests;

public class UserTests
{
    private static Organization Org(string id, OrganizationType type) =>
        new(id, type, "Org", "1", "Москва", OrganizationStatus.Active, DateTimeOffset.UtcNow);

    [Fact]
    public void Email_is_normalized()
    {
        var user = new User("u1", "  Director@Demo.RU ", "h", "Имя", null, false);
        Assert.Equal("director@demo.ru", user.Email);
    }

    [Fact]
    public void Seller_role_cannot_join_fulfillment()
    {
        var user = new User("u1", "a@b.ru", "h", "Имя", null, false);
        Assert.Throws<DomainException>(() => user.JoinOrganization(Org("o1", OrganizationType.Fulfillment), Role.SellerOwner));
    }

    [Fact]
    public void Duplicate_membership_is_rejected()
    {
        var user = new User("u1", "a@b.ru", "h", "Имя", null, false);
        var org = Org("o1", OrganizationType.Seller);
        user.JoinOrganization(org, Role.SellerOwner);
        Assert.Throws<DomainException>(() => user.JoinOrganization(org, Role.SellerManager));
    }

    [Fact]
    public void ResolveMembership_uses_header_or_falls_back_to_first()
    {
        var user = new User("u1", "a@b.ru", "h", "Имя", null, false);
        user.JoinOrganization(Org("o1", OrganizationType.Fulfillment), Role.FcOwner);
        user.JoinOrganization(Org("o2", OrganizationType.Fulfillment), Role.FcPicker);
        Assert.Equal(Role.FcPicker, user.ResolveMembership("o2")!.Role);
        Assert.Equal(Role.FcOwner, user.ResolveMembership("unknown")!.Role);
        Assert.Equal(Role.FcOwner, user.ResolveMembership(null)!.Role);
    }
}

public class OrganizationTests
{
    [Fact]
    public void Registered_organization_is_pending()
    {
        var org = Organization.Register("o", OrganizationType.Seller, " Имя ", "1", "М", DateTimeOffset.UtcNow, [Marketplace.Wb, Marketplace.Wb]);
        Assert.Equal(OrganizationStatus.Pending, org.Status);
        Assert.Equal("Имя", org.Name);
        Assert.Equal([Marketplace.Wb], org.Marketplaces);
    }

    [Theory]
    [InlineData(OrganizationType.Seller, Role.SellerOwner)]
    [InlineData(OrganizationType.Fulfillment, Role.FcOwner)]
    public void Owner_role_depends_on_type(OrganizationType type, Role expected) =>
        Assert.Equal(expected, RoleRules.OwnerRoleFor(type));
}
