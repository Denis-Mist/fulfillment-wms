namespace Wms.Application.Auth;

public interface IRegisterRequestValidator
{
    /// <summary>Пустой словарь — запрос корректен.</summary>
    IDictionary<string, string[]> Validate(RegisterRequest request);
}

public sealed class RegisterRequestValidator : IRegisterRequestValidator
{
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
        return errors;
    }
}
