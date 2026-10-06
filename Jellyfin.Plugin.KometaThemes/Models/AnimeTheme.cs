using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// An opening or ending of an anime.
/// </summary>
/// <param name="Id">Theme ID.</param>
/// <param name="Type">Opening or ending.</param>
/// <param name="Sequence">Number among themes of the same type. Missing when the anime has only one.</param>
/// <param name="Slug">Short code such as <c>OP1</c> or <c>ED1-TV</c>.</param>
/// <param name="Song">The song, when animethemes.moe knows it.</param>
/// <param name="Entries">Versions of the theme with the episodes they air in.</param>
public sealed record AnimeTheme(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("type")] ThemeType Type,
    [property: JsonPropertyName("sequence")] int? Sequence,
    [property: JsonPropertyName("slug")] string? Slug,
    [property: JsonPropertyName("song")] Song? Song,
    [property: JsonPropertyName("animethemeentries")] Collection<AnimeThemeEntry>? Entries);
