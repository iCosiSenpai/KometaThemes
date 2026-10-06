using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// A link from an anime to another site.
/// </summary>
/// <param name="ExternalId">ID on that site.</param>
/// <param name="Site">Site name, such as <c>AniList</c>.</param>
public sealed record Resource(
    [property: JsonPropertyName("external_id")] int? ExternalId,
    [property: JsonPropertyName("site")] string? Site);
