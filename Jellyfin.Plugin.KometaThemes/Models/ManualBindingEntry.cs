using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// The owner's match of a series, season or movie to an animethemes.moe entry, set from the anime
/// page. It wins over every automatic match.
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
    /// Gets or sets where the match was made: "Page" for the anime page, "ThemeFinder" for 1.x.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}
