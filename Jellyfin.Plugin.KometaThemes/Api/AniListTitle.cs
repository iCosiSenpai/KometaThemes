using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// AniList media title variants.
/// </summary>
/// <param name="Romaji">Romaji title.</param>
/// <param name="English">English title.</param>
/// <param name="Native">Native title.</param>
/// <param name="UserPreferred">User-preferred title.</param>
public record AniListTitle(
    [property: JsonPropertyName("romaji")] string? Romaji,
    [property: JsonPropertyName("english")] string? English,
    [property: JsonPropertyName("native")] string? Native,
    [property: JsonPropertyName("userPreferred")] string? UserPreferred);
