using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// A cover image.
/// </summary>
/// <param name="Facet">Image kind, such as <c>Large Cover</c>.</param>
/// <param name="Link">Image URL.</param>
public sealed record AnimeImage(
    [property: JsonPropertyName("facet")] string? Facet,
    [property: JsonPropertyName("link")] string? Link);
