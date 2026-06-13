using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// An anime theme API resource represents an OP or ED of an anime.
/// </summary>
/// <param name="Id">The primary key of the resource.</param>
/// <param name="Type">The type of theme (OP or ED).</param>
/// <param name="Sequence">The sequence number of the theme.</param>
/// <param name="Slug">The URL slug of the theme.</param>
/// <param name="Entries">The entries (versions) of the theme.</param>
public record AnimeTheme(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("type")] ThemeType Type,
    [property: JsonPropertyName("sequence")]
    int? Sequence,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("animethemeentries")]
    Collection<AnimeThemeEntry> Entries
);
