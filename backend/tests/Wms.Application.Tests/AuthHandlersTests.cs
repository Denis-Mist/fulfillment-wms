using NSubstitute;
using Wms.Application.Abstractions;
using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class AuthHandlersTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOrganizationRepository _orgs = Substitute.For<IOrganizationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();

    public AuthHandlersTests()
    {
        _tokens.CreateAccessToken(Arg.Any<User>()).Returns("token");
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hash:" + c.Arg<string>());
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(0) == "hash:" + c.ArgAt<string>(1));
        _ids.NewSuffix().Returns("abc");
        _orgs.GetByIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Organization>());
    }

    private RegisterHandler Register() => new(new RegisterRequestValidator(), _users, _orgs, _uow, _hasher, _tokens,
        new CurrentUserMapper(_orgs), _clock, _ids);

    private static RegisterRequest ValidRequest => new(OrganizationType.Seller,
        new RegisterOrganization("ООО Тест", "1", "Москва", null, [Marketplace.Wb]),
        new RegisterUser("Тест Тестов", "New@Test.ru", "+7", "secret"));

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        _users.FindByEmailAsync("a@b.ru", default).Returns(new User("u", "a@b.ru", "hash:right", "N", null, false));
        var handler = new LoginHandler(_users, _hasher, _tokens, new CurrentUserMapper(_orgs));
        var ex = await Assert.ThrowsAsync<AppException>(() => handler.HandleAsync(new LoginRequest("A@B.ru", "wrong"), default));
        Assert.Equal(401, ex.Status);
        Assert.Equal("Неверный email или пароль", ex.Title);
    }

    [Fact]
    public async Task Login_success_returns_token_and_user()
    {
        _users.FindByEmailAsync("a@b.ru", default).Returns(new User("u", "a@b.ru", "hash:right", "N", null, false));
        var handler = new LoginHandler(_users, _hasher, _tokens, new CurrentUserMapper(_orgs));
        var result = await handler.HandleAsync(new LoginRequest(" a@b.ru", "right"), default);
        Assert.Equal("token", result.AccessToken);
        Assert.Equal("u", result.User.Id);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_409()
    {
        _users.EmailExistsAsync("new@test.ru", default).Returns(true);
        var ex = await Assert.ThrowsAsync<AppException>(() => Register().HandleAsync(ValidRequest, default));
        Assert.Equal(409, ex.Status);
    }

    [Fact]
    public async Task Register_missing_fields_returns_400()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Register().HandleAsync(new RegisterRequest(OrganizationType.Seller, null, null), default));
        Assert.Equal(400, ex.Status);
        Assert.Equal("Заполнены не все обязательные поля", ex.Title);
    }

    [Fact]
    public async Task Register_creates_pending_org_and_owner()
    {
        Organization? org = null;
        User? user = null;
        _orgs.When(o => o.Add(Arg.Any<Organization>())).Do(c => org = c.Arg<Organization>());
        _users.When(u => u.Add(Arg.Any<User>())).Do(c => user = c.Arg<User>());

        await Register().HandleAsync(ValidRequest, default);

        Assert.Equal("org_r_abc", org!.Id);
        Assert.Equal(OrganizationStatus.Pending, org.Status);
        Assert.Equal("usr_r_abc", user!.Id);
        Assert.Equal("new@test.ru", user.Email);
        Assert.Equal(Role.SellerOwner, Assert.Single(user.Memberships).Role);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Context_resolver_returns_platform_admin_without_org()
    {
        _users.FindByIdAsync("adm", default).Returns(new User("adm", "a@b.ru", "h", "N", null, true));
        var ctx = await new RequestContextResolver(_users, _orgs).ResolveAsync("adm", "org_x", default);
        Assert.Equal(Role.PlatformAdmin, ctx!.Role);
        Assert.Null(ctx.OrganizationId);
    }

    [Fact]
    public void Paged_from_slices_items()
    {
        var page = Paged<int>.From(Enumerable.Range(1, 45), new PageQuery(Page: 3, PageSize: 20));
        Assert.Equal([41, 42, 43, 44, 45], page.Items);
        Assert.Equal(45, page.Total);
    }
}
