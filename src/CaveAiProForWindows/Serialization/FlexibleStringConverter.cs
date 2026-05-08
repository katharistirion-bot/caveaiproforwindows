using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Serialization;

/// <summary>
/// Gson may emit JSON numbers for fields modeled as <see cref="string"/> in C# (e.g. <c>surveyArchiveSchemaVersion: 2</c>).
/// Applies to every <see cref="string"/> (including nullable-annotated) when registered on <see cref="Services.ExplorationDataLoader"/> options.
/// Accepts null, strings, numbers (integer or floating), and booleans; skips other values and returns null.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return NumberToString(ref reader);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            default:
                if (!reader.TrySkip())
                    throw new JsonException($"Cannot skip JSON value while coercing token {reader.TokenType} to string.");
                return null;
        }
    }

    private static string NumberToString(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var l))
            return l.ToString(CultureInfo.InvariantCulture);
        if (reader.TryGetUInt64(out var ul))
            return ul.ToString(CultureInfo.InvariantCulture);
        if (reader.TryGetDouble(out var d))
            return d.ToString(CultureInfo.InvariantCulture);
        if (reader.TryGetDecimal(out var dec))
            return dec.ToString(CultureInfo.InvariantCulture);
        return Encoding.UTF8.GetString(reader.ValueSpan);
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
