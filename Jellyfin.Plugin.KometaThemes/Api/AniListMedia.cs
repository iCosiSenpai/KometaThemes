using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// AniList media title metadata.
/// </summary>
/// <param name="Id">AniList ID.</param>
/// <param name="IdMal">MyAnimeList ID.</param>
/// <param name="Title">Title fields.</param>
/// <param name="Synonyms">AniList synonyms.</param>
public record AniListMedia(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("idMal")] int? IdMal,
    [property: JsonPropertyName("title")] AniListTitle? Title,
    [property: JsonPropertyName("synonyms")] IReadOnlyList<string>? Synonyms);
