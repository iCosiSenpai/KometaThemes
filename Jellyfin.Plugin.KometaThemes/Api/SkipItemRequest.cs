using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Request body for manually skipping an item.
/// </summary>
public class SkipItemRequest
{
    /// <summary>
    /// Gets or sets the optional skip reason.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
