using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wms.Application.Common;

namespace Wms.Api.Infrastructure;

/// <summary>Даты как Date.toISOString() в моках: UTC с миллисекундами и суффиксом Z.</summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value : throw new JsonException("Некорректная дата");

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Sorting.Iso(value));
}
