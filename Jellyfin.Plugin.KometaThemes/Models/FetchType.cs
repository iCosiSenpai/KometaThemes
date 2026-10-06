namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// How many themes to download per media type.
/// </summary>
public enum FetchType
{
    /// <summary>Do not download this media type.</summary>
    None,

    /// <summary>Only the main theme: the first opening, or the first ending when openings are off.</summary>
    Single,

    /// <summary>Every theme, up to the per-season limit.</summary>
    All,

    /// <summary>Kept so 1.x configuration files still load; treated as <see cref="All"/>.</summary>
    AllPerSeason
}
