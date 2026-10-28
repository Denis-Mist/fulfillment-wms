using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Wms.Infrastructure.Common;

/// <summary>Enum ⇄ snake_case строка (как во фронте: fc_owner, in_transit).</summary>
public static class SnakeCase
{
    public static string ToSnake<T>(T value) where T : struct, Enum => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    public static T Parse<T>(string value) where T : struct, Enum =>
        Enum.GetValues<T>().First(v => ToSnake(v) == value);
}

public sealed class SnakeCaseEnumConverter<T>() : ValueConverter<T, string>(v => SnakeCase.ToSnake(v), v => SnakeCase.Parse<T>(v))
    where T : struct, Enum;
