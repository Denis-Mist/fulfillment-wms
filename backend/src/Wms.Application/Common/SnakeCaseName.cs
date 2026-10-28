using System.Text;

namespace Wms.Application.Common;

/// <summary>Имена enum в формате контракта (snake_case), как сериализует Api.</summary>
public static class SnakeCaseName
{
    public static string Of<T>(T value) where T : struct, Enum => Convert(value.ToString());

    public static bool TryParse<T>(string? value, out T result) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
            if (Of(candidate) == value) { result = candidate; return true; }
        result = default;
        return false;
    }

    private static string Convert(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
