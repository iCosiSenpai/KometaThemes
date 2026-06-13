using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Represents an image associated with an AnimeThemes resource.
/// </summary>
/// <param name="Id">The primary key of the image resource.</param>
/// <param name="Facet">The image facet, such as Small Cover or Large Cover.</param>
/// <param name="Path">The storage path of the image.</param>
/// <param name="Link">The public image URL.</param>
public record AnimeImage(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("facet")] string Facet,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("link")] string Link);
