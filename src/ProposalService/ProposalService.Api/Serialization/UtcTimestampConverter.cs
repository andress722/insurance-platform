using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProposalService.Api.Serialization;

/// <summary>
/// Publishes timestamps exactly as documented in docs/04: ISO 8601 in UTC with the 'Z' designator,
/// instead of the '+00:00' offset the default converter emits.
/// </summary>
public sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
