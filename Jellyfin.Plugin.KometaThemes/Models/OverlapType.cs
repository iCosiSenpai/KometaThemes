using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Types of overlap between episode and theme.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverlapType
{
    /// <summary>
    /// No overlap.
    /// </summary>
    None,

    /// <summary>
    /// Some overlap.
    /// </summary>
    Transition,

    /// <summary>
    /// Complete overlap.
    /// </summary>
    Over
}
