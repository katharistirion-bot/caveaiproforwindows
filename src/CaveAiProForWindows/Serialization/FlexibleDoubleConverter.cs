using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Serialization;

/// <summary>
/// Gson/Android sometimes emits <c>""</c> for optional numeric GPS fields; System.Text.Json cannot map that to <see cref="double"/>? by default.
/// Accepts JSON null, numbers, numeric strings, and empty/whitespace strings (as null).
/// Any other token (object, array, bool) is skipped and treated as null so noisy exports do not abort the whole import.
/// </summary>
public sealed class FlexibleDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return null;
                return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d
                    : null;
            }
            case JsonTokenType.Number:
                return reader.TryGetDouble(out var n) ? n : null;
            default:
                if (!reader.TrySkip())
                    throw new JsonException($"Cannot skip JSON value while coercing token {reader.TokenType} to nullable double.");
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}
