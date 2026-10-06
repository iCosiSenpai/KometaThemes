using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// One encode of a theme.
/// </summary>
/// <param name="Id">Video ID.</param>
/// <param name="Basename">File name on the CDN.</param>
/// <param name="Source">Rip source.</param>
/// <param name="Overlap">Overlap with episode content.</param>
/// <param name="Link">Video URL.</param>
/// <param name="Creditless">Whether the video has no credits.</param>
/// <param name="Audio">Extracted audio track.</param>
public sealed record Video(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("basename")] string? Basename,
    [property: JsonPropertyName("source")] VideoSource Source,
    [property: JsonPropertyName("overlap")] OverlapType Overlap,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("nc")] bool Creditless,
    [property: JsonPropertyName("audio")] Audio? Audio)
{
    /// <summary>Gets the vertical resolution.</summary>
    [JsonPropertyName("resolution")]
    public int? Resolution { get; init; }

    /// <summary>Gets the file size in bytes.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; init; }
}
