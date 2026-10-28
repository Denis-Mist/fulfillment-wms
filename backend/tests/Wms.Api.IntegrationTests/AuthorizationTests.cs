using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using Wms.Api.Infrastructure;
using Wms.Application.Auth;
using Wms.Domain.Enums;

namespace Wms.Api.IntegrationTests;

public class AuthorizationTests
{
    [Theory]
    [InlineData(Role.FcPicker, Policies.Worker, true)]
    [InlineData(Role.SellerOwner, Policies.Fulfillment, false)]
    [InlineData(Role.FcManager, Policies.FulfillmentManagement, true)]
    [InlineData(Role.PlatformAdmin, Policies.PlatformAdmin, true)]
    public async Task Role_requirement_matches_policy(Role role, string policy, bool expected)
    {
        var accessor = Substitute.For<IRequestContextAccessor>();
        accessor.GetAsync().Returns(new RequestContext("u", "o", role, OrganizationStatus.Active));
        var requirement = new RoleRequirement(Policies.Roles[policy]);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), null);
        await new RoleRequirementHandler(accessor).HandleAsync(context);
        Assert.Equal(expected, context.HasSucceeded);
    }
}
