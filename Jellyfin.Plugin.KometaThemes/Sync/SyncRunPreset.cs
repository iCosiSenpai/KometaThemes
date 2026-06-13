using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Preset sync/download runs exposed to the settings page.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SyncRunPreset
{
    /// <summary>
    /// Download all opening themes as audio.
    /// </summary>
    AllOPAudio,

    /// <summary>
    /// Download all ending themes as audio.
    /// </summary>
    AllEDAudio,

    /// <summary>
    /// Download all opening and ending themes as audio.
    /// </summary>
    AllOPEDAudio,

    /// <summary>
    /// Download all opening and ending themes as audio and video.
    /// </summary>
    AllOPEDAudioVideo
}
