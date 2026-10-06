using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// The audio track animethemes.moe extracts from a video.
/// </summary>
/// <param name="Id">Audio ID.</param>
/// <param name="Link">Audio URL.</param>
public sealed record Audio(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("link")] string? Link)
{
    /// <summary>Gets the file size in bytes.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; init; }
}
