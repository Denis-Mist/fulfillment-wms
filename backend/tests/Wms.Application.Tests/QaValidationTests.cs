using Wms.Application.Auth;
using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Tests;

public class QaValidationTests
{
    private static RegisterRequest Request(string email, string password, string name = "ООО Тест", string fullName = "Иван Тестов") =>
        new(OrganizationType.Seller, new RegisterOrganization(name, "7700000000", "Москва", null, null),
            new RegisterUser(fullName, email, null, password));

    [Fact]
    public void Valid_request_has_no_errors() =>
        Assert.Empty(new RegisterRequestValidator().Validate(Request("a@b.ru", "secret")));

    [Theory]
    [InlineData("plain", "user.email")]
    [InlineData("a@b", "user.email")]
    [InlineData("a b@c.ru", "user.email")]
    public void Invalid_email(string email, string field) =>
        Assert.Contains(field, new RegisterRequestValidator().Validate(Request(email, "secret")).Keys);

    [Fact]
    public void Short_password_rejected_six_is_ok()
    {
        var v = new RegisterRequestValidator();
        Assert.Equal(["Минимум 6 символов"], v.Validate(Request("a@b.ru", "1"))["user.password"]);
        Assert.Empty(v.Validate(Request("a@b.ru", "123456")));
    }

    [Fact]
    public void Too_long_fields()
    {
        var s = new string('x', 257);
        var errors = new RegisterRequestValidator().Validate(Request($"{s}@b.ru", "secret", s, s));
        Assert.Contains("organization.name", errors.Keys);
        Assert.Contains("user.fullName", errors.Keys);
        Assert.Contains("user.email", errors.Keys);
        Assert.False(RegisterRequestValidator.OnlyMissing(errors));
    }

    [Fact]
    public void Text_limits_policy()
    {
        Assert.Equal(TextLimits.Default, TextLimits.MaxFor("name"));
        Assert.Equal(TextLimits.LongText, TextLimits.MaxFor("Comment"));
        var errors = new Dictionary<string, string[]>();
        TextLimits.Check(errors, "a.name", new string('x', 256));
        Assert.Empty(errors);
        TextLimits.Check(errors, "a.name", new string('x', 257));
        Assert.Equal(["Не более 256 символов"], errors["a.name"]);
    }
}
