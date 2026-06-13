using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// JSON converter that accepts both enum names and numeric values for fetch types.
/// </summary>
public sealed class FetchTypeJsonConverter : JsonConverter<FetchType>
{
    /// <inheritdoc />
    public override FetchType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
        {
            return ParseNumeric(numericValue);
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return FetchType.None;
            }

            if (int.TryParse(stringValue, out numericValue))
            {
                return ParseNumeric(numericValue);
            }

            if (Enum.TryParse<FetchType>(stringValue, true, out var parsedValue))
            {
                return parsedValue;
            }
        }

        throw new JsonException($"Unable to convert value to {nameof(FetchType)}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FetchType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    private static FetchType ParseNumeric(int numericValue)
    {
        if (Enum.IsDefined(typeof(FetchType), numericValue))
        {
            return (FetchType)numericValue;
        }

        throw new JsonException($"Invalid numeric value {numericValue} for {nameof(FetchType)}.");
    }
}
