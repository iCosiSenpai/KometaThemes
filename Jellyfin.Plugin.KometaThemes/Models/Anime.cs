using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Represents an anime from the AnimeThemes API.
/// </summary>
/// <param name="Id">The primary key of the resource.</param>
/// <param name="Name">The primary title of the anime.</param>
/// <param name="Slug">The URL slug of the anime.</param>
/// <param name="Year">The premiere year of the anime.</param>
/// <param name="Themes">The themes of the anime.</param>
/// <param name="Resources">External resources linked to this anime.</param>
public record Anime(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("animethemes")]
    Collection<AnimeTheme> Themes,
    [property: JsonPropertyName("resources")]
    Collection<Resource>? Resources
)
{
    /// <summary>
    /// Gets the premiere season of the anime.
    /// </summary>
    [JsonPropertyName("season")]
    public string? Season { get; init; }

    /// <summary>
    /// Gets the media format of the anime.
    /// </summary>
    [JsonPropertyName("media_format")]
    public string? MediaFormat { get; init; }

    /// <summary>
    /// Gets the synopsis of the anime.
    /// </summary>
    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; init; }

    /// <summary>
    /// Gets image resources for the anime.
    /// </summary>
    [JsonPropertyName("images")]
    public Collection<AnimeImage>? Images { get; init; }

    /// <summary>
    /// Gets the external ID for a given site.
    /// </summary>
    /// <param name="site">The site name (e.g. "AniDB", "MyAnimeList").</param>
    /// <returns>The external ID if found, null otherwise.</returns>
    public int? GetExternalId(string site)
    {
        return Resources?
            .Where(r => r.Site == site)
            .Select(r => r.ExternalId)
            .FirstOrDefault();
    }
}
