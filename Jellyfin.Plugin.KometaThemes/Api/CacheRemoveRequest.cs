using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Request body for removing one cache entry.
/// </summary>
public class CacheRemoveRequest
{
    /// <summary>
    /// Gets or sets the raw cache key to remove.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
}
