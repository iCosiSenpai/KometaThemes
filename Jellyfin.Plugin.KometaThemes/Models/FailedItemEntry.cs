using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// A library item that could not be resolved or whose theme download failed,
/// surfaced in the dashboard so the user can retry or blacklist it.
/// </summary>
public class FailedItemEntry
{
    /// <summary>
    /// Gets or sets the Jellyfin item ID.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item display name at the time of the failure.
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
    /// Gets or sets why the item failed.
    /// </summary>
    [JsonPropertyName("reason")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FailedItemReason Reason { get; set; }

    /// <summary>
    /// Gets or sets the error message of the last failed attempt, if any.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the UTC time of the last failed attempt.
    /// </summary>
    [JsonPropertyName("lastAttemptUtc")]
    public DateTime LastAttemptUtc { get; set; }

    /// <summary>
    /// Gets or sets how many times this item has failed.
    /// </summary>
    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }
}
