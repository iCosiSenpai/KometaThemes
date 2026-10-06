using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Whether a theme video overlaps episode content.
/// </summary>
[JsonConverter(typeof(OverlapTypeConverter))]
public enum OverlapType
{
    /// <summary>Clean theme.</summary>
    None,

    /// <summary>Episode content fades into or out of the theme.</summary>
    Transition,

    /// <summary>Episode content plays over the theme.</summary>
    Over
}

/// <summary>
/// Lenient converter for <see cref="OverlapType"/>.
/// </summary>
public sealed class OverlapTypeConverter : LenientEnumConverter<OverlapType>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OverlapTypeConverter"/> class.
    /// </summary>
    public OverlapTypeConverter()
        : base(OverlapType.None)
    {
    }
}
