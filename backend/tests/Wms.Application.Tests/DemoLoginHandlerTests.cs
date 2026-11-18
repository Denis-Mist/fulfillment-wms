using Microsoft.Extensions.Options;
using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Application.Demo;
using Wms.Domain.Entities;

namespace Wms.Application.Tests;

public class DemoLoginHandlerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOrganizationRepository _orgs = Substitute.For<IOrganizationRepository>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();

    public DemoLoginHandlerTests()
    {
        _tokens.CreateAccessToken(Arg.Any<User>(), Arg.Any<AccessTokenRequest?>()).Returns("demo-token");
        _orgs.GetByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Organization>());
    }

    private DemoLoginHandler Handler(bool enabled) =>
        new(Options.Create(new DemoOptions { Enabled = enabled, TokenLifetimeMinutes = 120 }),
            _users, _tokens, new CurrentUserMapper(_orgs));

    [Fact]
    public async Task Disabled_returns_404()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Handler(false).HandleAsync(new DemoLoginRequest("fc_owner"), default));
        Assert.Equal(404, ex.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown_role")]
    public async Task Unknown_persona_returns_422(string? persona)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => Handler(true).HandleAsync(new DemoLoginRequest(persona), default));
        Assert.Equal(422, ex.Status);
        Assert.Equal("Неизвестная персона", ex.Title);
    }

    [Fact]
    public async Task Success_issues_short_lived_demo_token()
    {
        var user = new User("usr_fc1_1", "director@demo.ru", "hash", "Ирина", null, false);
        _users.FindByEmailAsync("director@demo.ru", default).Returns(user);

        var result = await Handler(true).HandleAsync(new DemoLoginRequest("FC_OWNER"), default);

        Assert.Equal("demo-token", result.AccessToken);
        Assert.True(result.User.IsDemo);
        Assert.Equal("usr_fc1_1", result.User.Id);
        _tokens.Received(1).CreateAccessToken(user, Arg.Is<AccessTokenRequest?>(r =>
            r != null && r.LifetimeMinutes == 120
            && r.ExtraClaims != null
            && r.ExtraClaims[DemoClaims.Type] == DemoClaims.Value));
    }
}
