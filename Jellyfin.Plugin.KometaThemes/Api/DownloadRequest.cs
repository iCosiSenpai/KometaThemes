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
}
