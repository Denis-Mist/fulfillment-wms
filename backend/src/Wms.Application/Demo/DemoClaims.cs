using System.Security.Claims;

namespace Wms.Application.Demo;

public static class DemoClaims
{
    public const string Type = "demo";
    public const string Value = "true";

    public static bool IsDemo(ClaimsPrincipal? user) =>
        user is not null && string.Equals(user.FindFirst(Type)?.Value, Value, StringComparison.Ordinal);
}
