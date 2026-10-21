using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Another title of an anime.
/// </summary>
/// <param name="Text">The title.</param>
public sealed record AnimeSynonym([property: JsonPropertyName("text")] string? Text);

/// <summary>
/// A franchise on animethemes.moe, such as every season and movie of Kaguya-sama.
/// </summary>
/// <param name="Id">Series ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Slug">Slug.</param>
public sealed record AnimeSeries(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("slug")] string? Slug);

/// <summary>
/// An anime entry on animethemes.moe.
/// </summary>
/// <param name="Id">Anime ID.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL slug.</param>
/// <param name="Year">Premiere year.</param>
/// <param name="Themes">Openings and endings.</param>
/// <param name="Resources">Links to other sites, which carry their IDs.</param>
public sealed record Anime(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("animethemes")] Collection<AnimeTheme>? Themes,
    [property: JsonPropertyName("resources")] Collection<Resource>? Resources)
{
    /// <summary>Gets the premiere season, such as <c>Winter</c>.</summary>
    [JsonPropertyName("season")]
    public string? Season { get; init; }

    /// <summary>Gets the format, such as <c>TV</c> or <c>Movie</c>.</summary>
    [JsonPropertyName("media_format")]
    public string? MediaFormat { get; init; }

    /// <summary>Gets the cover images.</summary>
    [JsonPropertyName("images")]
    public Collection<AnimeImage>? Images { get; init; }

    /// <summary>Gets the other titles and abbreviations, such as an English title or <c>DanMachi</c>.</summary>
    [JsonPropertyName("animesynonyms")]
    public Collection<AnimeSynonym>? Synonyms { get; init; }

    /// <summary>Gets the franchises animethemes.moe groups this anime into.</summary>
    [JsonPropertyName("series")]
    public Collection<AnimeSeries>? Series { get; init; }

    /// <summary>Gets every title of the anime: its name first, then its synonyms.</summary>
    [JsonIgnore]
    public System.Collections.Generic.IEnumerable<string> Titles
        => new[] { Name }.Concat((Synonyms ?? []).Select(synonym => synonym.Text)).Where(title => !string.IsNullOrWhiteSpace(title)).Select(title => title!);

    /// <summary>
    /// Gets this anime's ID on another site.
    /// </summary>
    /// <param name="site">Site name, compared case-insensitively.</param>
    /// <returns>The ID, or null.</returns>
    public int? GetExternalId(string site)
        => Resources?.FirstOrDefault(r => string.Equals(r.Site, site, StringComparison.OrdinalIgnoreCase) && r.ExternalId.HasValue)?.ExternalId;

    /// <summary>
    /// Gets a cover image URL.
    /// </summary>
    /// <param name="large">Whether to prefer the large cover.</param>
    /// <returns>The URL, or null.</returns>
    public string? GetCover(bool large)
    {
        var images = Images ?? [];
        var preferred = large ? "Large Cover" : "Small Cover";
        var fallback = large ? "Small Cover" : "Large Cover";
        return (images.FirstOrDefault(i => string.Equals(i.Facet, preferred, StringComparison.OrdinalIgnoreCase))
            ?? images.FirstOrDefault(i => string.Equals(i.Facet, fallback, StringComparison.OrdinalIgnoreCase)))?.Link;
    }
}
