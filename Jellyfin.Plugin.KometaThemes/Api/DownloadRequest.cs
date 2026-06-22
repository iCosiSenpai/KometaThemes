using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Search;

/// <summary>
/// Download request model.
/// </summary>
public class DownloadRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadRequest"/> class.
    /// </summary>
    /// <param name="urls">The theme URLs selected by the Theme Finder UI.</param>
    [JsonConstructor]
    public DownloadRequest(Collection<DownloadThemeItem>? urls)
    {
        Urls = urls ?? new Collection<DownloadThemeItem>();
    }

    /// <summary>
    /// Gets the list of theme URLs to download.
    /// </summary>
    [JsonPropertyName("urls")]
    public Collection<DownloadThemeItem> Urls { get; }

    /// <summary>
    /// Gets or sets the AnimeThemes anime ID selected in the Theme Finder.
    /// </summary>
    [JsonPropertyName("animeId")]
    public int? AnimeId { get; set; }

    /// <summary>
    /// Gets or sets the anime display name selected in the Theme Finder.
    /// </summary>
    [JsonPropertyName("animeName")]
    public string AnimeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the anime slug selected in the Theme Finder.
    /// </summary>
    [JsonPropertyName("animeSlug")]
    public string AnimeSlug { get; set; } = string.Empty;
}
