using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Reads an enum from its name without failing on values this version does not know.
/// </summary>
/// <remarks>
/// animethemes.moe adds values over time (a new theme type, a new video source). With the stock
/// <see cref="JsonStringEnumConverter"/> one unknown value threw, and because external IDs are looked up
/// in batches of up to a hundred, a single anime with a new value made the whole batch unresolved.
/// Unknown names, numbers out of range and nulls now map to a fallback the callers filter out.
/// </remarks>
/// <typeparam name="TEnum">The enum type.</typeparam>
public abstract class LenientEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly TEnum _fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="LenientEnumConverter{TEnum}"/> class.
    /// </summary>
    /// <param name="fallback">Value used for anything that does not name a member.</param>
    protected LenientEnumConverter(TEnum fallback)
    {
        _fallback = fallback;
    }

    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                return !string.IsNullOrWhiteSpace(text)
                    && !int.TryParse(text, out _)
                    && Enum.TryParse<TEnum>(text.Trim(), ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed)
                        ? parsed
                        : _fallback;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                var value = (TEnum)Enum.ToObject(typeof(TEnum), number);
                return Enum.IsDefined(value) ? value : _fallback;
            default:
                reader.Skip();
                return _fallback;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
