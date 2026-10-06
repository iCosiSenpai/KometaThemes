using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// The song behind a theme, as listed by animethemes.moe.
/// </summary>
/// <param name="Id">Song ID.</param>
/// <param name="Title">Song title. Can be missing for unidentified songs.</param>
/// <param name="Artists">Credited artists, in billing order.</param>
public sealed record Song(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("artists")] Collection<Artist>? Artists)
{
    /// <summary>
    /// Gets the artists joined for display, or an empty string when none are credited.
    /// </summary>
    [JsonIgnore]
    public string ArtistNames => string.Join(", ", (Artists ?? []).Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
}

/// <summary>
/// A credited artist.
/// </summary>
/// <param name="Id">Artist ID.</param>
/// <param name="Name">Artist name.</param>
/// <param name="Slug">Artist slug.</param>
public sealed record Artist(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string? Slug);
