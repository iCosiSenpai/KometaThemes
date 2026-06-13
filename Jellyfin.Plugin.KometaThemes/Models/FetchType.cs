using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Enum describing the fetch types for themes.
/// </summary>
[JsonConverter(typeof(FetchTypeJsonConverter))]
public enum FetchType
{
    /// <summary>
    /// Don't fetch any themes.
    /// </summary>
    None,

    /// <summary>
    /// Fetch only the first/best theme.
    /// </summary>
    Single,

    /// <summary>
    /// Fetch all available themes.
    /// </summary>
    All,

    /// <summary>
    /// Fetch all themes for the detected season (multi-OP/ED per season).
    /// </summary>
    AllPerSeason
}
