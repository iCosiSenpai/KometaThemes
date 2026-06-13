using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Controls what to download when an item has no existing themes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissingThemeFallbackMode
{
    /// <summary>
    /// Use normal per-media-type configuration.
    /// </summary>
    None,

    /// <summary>
    /// Download all Opening themes (audio) when missing.
    /// </summary>
    AllOPs,

    /// <summary>
    /// Download all Ending themes (audio) when missing.
    /// </summary>
    AllEDs,

    /// <summary>
    /// Download all video themes (webm) when missing.
    /// </summary>
    AllVideos,

    /// <summary>
    /// Download all OPs + EDs + Videos when missing.
    /// </summary>
    AllOPsEDsVideos
}
