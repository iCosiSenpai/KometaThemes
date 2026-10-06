using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Kind of theme song.
/// </summary>
[JsonConverter(typeof(ThemeTypeConverter))]
public enum ThemeType
{
    /// <summary>Opening.</summary>
    OP,

    /// <summary>Ending.</summary>
    ED,

    /// <summary>Anything else animethemes.moe may list, such as insert songs. Never downloaded.</summary>
    Other
}

/// <summary>
/// Lenient converter for <see cref="ThemeType"/>.
/// </summary>
public sealed class ThemeTypeConverter : LenientEnumConverter<ThemeType>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeTypeConverter"/> class.
    /// </summary>
    public ThemeTypeConverter()
        : base(ThemeType.Other)
    {
    }
}
