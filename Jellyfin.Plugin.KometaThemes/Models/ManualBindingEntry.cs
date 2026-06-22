using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// A manual item-to-anime binding created by the user through the Theme Finder.
/// Takes precedence over automatic provider/title resolution.
/// </summary>
public class ManualBindingEntry
{
    /// <summary>
    /// Gets or sets the Jellyfin item ID.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the AnimeThemes anime ID.
    /// </summary>
    [JsonPropertyName("animeId")]
    public int AnimeId { get; set; }

    /// <summary>
    /// Gets or sets the anime display name at the time of binding.
    /// </summary>
    [JsonPropertyName("animeName")]
    public string AnimeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the anime slug.
    /// </summary>
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time when the binding was created.
    /// </summary>
    [JsonPropertyName("boundAt")]
    public DateTime BoundAt { get; set; }

    /// <summary>
    /// Gets or sets the source of the binding (e.g. "ThemeFinder").
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}
