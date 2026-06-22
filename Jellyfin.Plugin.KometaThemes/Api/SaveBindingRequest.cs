namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Request body for saving a manual item-to-anime binding.
/// </summary>
public sealed class SaveBindingRequest
{
    /// <summary>
    /// Gets or sets the AnimeThemes anime ID.
    /// </summary>
    public int AnimeId { get; set; }

    /// <summary>
    /// Gets or sets the anime display name.
    /// </summary>
    public string AnimeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the anime slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the binding source.
    /// </summary>
    public string? Source { get; set; }
}
