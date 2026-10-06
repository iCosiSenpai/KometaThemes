using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Response of the anime listing endpoint.
/// </summary>
/// <param name="Anime">Anime on this page.</param>
public sealed record AnimeResponse([property: JsonPropertyName("anime")] Collection<Anime>? Anime);

/// <summary>
/// Response of the search endpoint.
/// </summary>
/// <param name="Search">Results grouped by kind.</param>
public sealed record SearchResponse([property: JsonPropertyName("search")] SearchResults? Search);

/// <summary>
/// Search results.
/// </summary>
/// <param name="Anime">Matching anime.</param>
public sealed record SearchResults([property: JsonPropertyName("anime")] Collection<Anime>? Anime);
