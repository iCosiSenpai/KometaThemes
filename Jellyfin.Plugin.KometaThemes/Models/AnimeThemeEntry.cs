using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// One version of a theme.
/// </summary>
/// <param name="Id">Entry ID.</param>
/// <param name="Version">Version number, missing for the first.</param>
/// <param name="Episodes">Episodes this version airs in, for example <c>2, 4-12</c>.</param>
/// <param name="Nsfw">Whether the video is flagged not safe for work.</param>
/// <param name="Spoiler">Whether the video spoils the story.</param>
/// <param name="Videos">Available encodes.</param>
public sealed record AnimeThemeEntry(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("episodes")] string? Episodes,
    [property: JsonPropertyName("nsfw")] bool Nsfw,
    [property: JsonPropertyName("spoiler")] bool Spoiler,
    [property: JsonPropertyName("videos")] Collection<Video>? Videos);
