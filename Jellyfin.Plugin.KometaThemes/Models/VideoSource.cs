using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Where a theme video was ripped from.
/// </summary>
[JsonConverter(typeof(VideoSourceConverter))]
public enum VideoSource
{
    /// <summary>Not reported or not known to this version.</summary>
    Unknown,

    /// <summary>Web stream.</summary>
    WEB,

    /// <summary>TV broadcast.</summary>
    RAW,

    /// <summary>Blu-ray.</summary>
    BD,

    /// <summary>DVD.</summary>
    DVD,

    /// <summary>VHS.</summary>
    VHS,

    /// <summary>LaserDisc.</summary>
    LD
}

/// <summary>
/// Lenient converter for <see cref="VideoSource"/>.
/// </summary>
public sealed class VideoSourceConverter : LenientEnumConverter<VideoSource>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VideoSourceConverter"/> class.
    /// </summary>
    public VideoSourceConverter()
        : base(VideoSource.Unknown)
    {
    }
}
