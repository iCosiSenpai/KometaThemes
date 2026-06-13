using System.Text.Json.Serialization;
using Jellyfin.Plugin.KometaThemes.Sync;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Request body for a preset sync run.
/// </summary>
public sealed class SyncRunRequest
{
    /// <summary>
    /// Gets or sets the preset to run.
    /// </summary>
    [JsonPropertyName("preset")]
    public SyncRunPreset Preset { get; set; }
}
