using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Search;

/// <summary>
/// Individual theme download item.
/// </summary>
public class DownloadThemeItem
{
    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type (audio/video).
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the theme display name.
    /// </summary>
    [JsonPropertyName("themeName")]
    public string ThemeName { get; set; } = string.Empty;
}
