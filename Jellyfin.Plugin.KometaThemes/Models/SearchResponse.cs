using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Represents a response to the search endpoint.
/// </summary>
/// <param name="Search">The search results.</param>
public record SearchResponse(
    [property: JsonPropertyName("search")] SearchResults Search
);
