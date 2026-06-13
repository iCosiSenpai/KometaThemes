using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// A library item that the user permanently excluded from automatic KometaThemes matching.
/// </summary>
public class SkippedItemEntry
{
    /// <summary>
    /// Gets or sets the Jellyfin item ID.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item display name when it was skipped.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyfin item type.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    [JsonPropertyName("productionYear")]
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the user-entered skip reason.
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time when this item was skipped.
    /// </summary>
    [JsonPropertyName("skippedUtc")]
    public DateTime SkippedUtc { get; set; }
}
