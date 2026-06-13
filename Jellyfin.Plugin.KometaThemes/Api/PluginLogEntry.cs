using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// A single parsed plugin log entry from the Jellyfin server log.
/// </summary>
public class PluginLogEntry
{
    /// <summary>
    /// Gets or sets the entry timestamp; null when the raw timestamp could not be parsed.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the raw timestamp string as it appears in the log file.
    /// </summary>
    [JsonPropertyName("rawTimestamp")]
    public string RawTimestamp { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the three-letter level (INF, WRN, ERR, DBG...).
    /// </summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full logger category.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the message, including any exception/stack trace continuation lines.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
