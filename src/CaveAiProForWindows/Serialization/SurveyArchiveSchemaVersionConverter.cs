using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Serialization;

/// <summary>
/// Android models surveyArchiveSchemaVersion as Int; Windows keeps a string in memory.
/// Writes a JSON number when the value is an integer so Gson import does not fail.
/// </summary>
public sealed class SurveyArchiveSchemaVersionConverter : JsonConverter<string?>
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
                if (reader.TryGetInt64(out var l))
                    return l.ToString(CultureInfo.InvariantCulture);
                if (reader.TryGetDouble(out var d))
                    return d.ToString(CultureInfo.InvariantCulture);
                return reader.GetDecimal().ToString(CultureInfo.InvariantCulture);
            default:
                if (!reader.TrySkip())
                    throw new JsonException($"Cannot coerce {reader.TokenType} to surveyArchiveSchemaVersion.");
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            writer.WriteNumberValue(n);
            return;
        }

        writer.WriteStringValue(value);
    }
}