using System.Text.RegularExpressions;
using Wms.Application.Common;

namespace Wms.Application.Auth;

public interface IRegisterRequestValidator
{
    /// <summary>Пустой словарь — запрос корректен.</summary>
    IDictionary<string, string[]> Validate(RegisterRequest request);
}

/// <summary>Правила формы регистрации фронтенда (RegisterPage.vue, zod): email, пароль от 6 символов; плюс лимиты колонок БД.</summary>
public sealed partial class RegisterRequestValidator : IRegisterRequestValidator
{
    public const int MinPasswordLength = 6;
    public const int MaxPasswordLength = 128;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static bool IsEmail(string value) => EmailPattern().IsMatch(value.Trim());

    public IDictionary<string, string[]> Validate(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        void Require(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) errors[field] = ["Обязательное поле"];
        }
        Require(request.Organization?.Name, "organization.name");
        Require(request.User?.Email, "user.email");
        Require(request.User?.Password, "user.password");
        Require(request.User?.FullName, "user.fullName");

        var email = request.User?.Email;
        if (!string.IsNullOrWhiteSpace(email) && !IsEmail(email)) errors.TryAdd("user.email", ["Некорректный email"]);
        var password = request.User?.Password;
        if (!string.IsNullOrEmpty(password) && password.Length < MinPasswordLength)
            errors.TryAdd("user.password", [$"Минимум {MinPasswordLength} символов"]);
        TextLimits.Check(errors, "user.password", password, MaxPasswordLength);

        TextLimits.Check(errors, "organization.name", request.Organization?.Name?.Trim());
        TextLimits.Check(errors, "organization.inn", request.Organization?.Inn, 16);
        TextLimits.Check(errors, "organization.city", request.Organization?.City, 128);
        TextLimits.Check(errors, "user.fullName", request.User?.FullName?.Trim());
        TextLimits.Check(errors, "user.email", email?.Trim());
        TextLimits.Check(errors, "user.phone", request.User?.Phone, 32);
        return errors;
    }

    /// <summary>Все ошибки — «обязательное поле»: тогда заголовок как в моке.</summary>
    public static bool OnlyMissing(IDictionary<string, string[]> errors) =>
        errors.Values.All(v => v is ["Обязательное поле"]);
}
