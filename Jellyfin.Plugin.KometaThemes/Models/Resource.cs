using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// An external resource linked to an anime (e.g. AniDB, MyAnimeList, AniList).
/// </summary>
/// <param name="Id">The primary key of the resource.</param>
/// <param name="ExternalId">The external ID on the target site.</param>
/// <param name="Site">The name of the target site (e.g. "AniDB", "MyAnimeList").</param>
/// <param name="Link">The URL to the resource on the target site.</param>
public record Resource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("external_id")]
    int? ExternalId,
    [property: JsonPropertyName("site")] string? Site,
    [property: JsonPropertyName("link")] string? Link
);
