using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Represents the search results containing anime matches.
/// </summary>
/// <param name="Anime">List of found anime.</param>
public record SearchResults(
    [property: JsonPropertyName("anime")] Collection<Anime> Anime
);
