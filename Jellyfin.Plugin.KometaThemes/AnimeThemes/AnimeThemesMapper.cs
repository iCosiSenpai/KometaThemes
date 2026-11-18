using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.AnimeThemes;

/// <summary>
/// Reads the GraphQL answers of animethemes.moe into the models the rest of the plugin reads.
/// </summary>
/// <remarks>
/// <para>
/// The models keep the names of the retired REST API: they are what every caller uses, and they are
/// also the shape already written into the resolution cache on disk. This class is then the only
/// place that knows both APIs, including their different spellings of the same value
/// (<c>ANILIST</c> against <c>AniList</c>, <c>TRANS</c> against <c>Transition</c>,
/// <c>WINTER</c> against <c>Winter</c>): those words are compared by
/// <see cref="Resolving.SeasonMatcher"/>, <see cref="Resolving.TitleMatcher"/>,
/// <see cref="Api.Sites"/> and the cover lookup, so they must read exactly as before.
/// </para>
/// <para>
/// Every field is optional here: a search result carries theme stubs only, a franchise member carries
/// no themes at all, and the API adds fields over time. Anything missing maps to null or to the
/// fallback the models already document.
/// </para>
/// </remarks>
internal static class AnimeThemesMapper
{
    /// <summary>Site names as the REST API spelled them, keyed by the GraphQL enum.</summary>
    private static readonly Dictionary<string, string> SiteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ANIDB"] = "AniDB",
        ["ANILIST"] = "AniList",
        ["MAL"] = "MyAnimeList",
        ["KITSU"] = "Kitsu",
        ["ANN"] = "Anime News Network",
        ["ANIME_PLANET"] = "Anime-Planet",
        ["OFFICIAL_SITE"] = "Official Website",
        ["X"] = "X",
        ["SPOTIFY"] = "Spotify",
        ["YOUTUBE"] = "YouTube",
        ["YOUTUBE_MUSIC"] = "YouTube Music",
        ["APPLE_MUSIC"] = "Apple Music",
        ["AMAZON_MUSIC"] = "Amazon Music",
        ["NETFLIX"] = "Netflix",
        ["HULU"] = "Hulu",
        ["CRUNCHYROLL"] = "Crunchyroll",
        ["HIDIVE"] = "HIDIVE",
        ["DISNEY_PLUS"] = "Disney+",
        ["AMAZON_PRIME_VIDEO"] = "Amazon Prime Video",
        ["INSTAGRAM"] = "Instagram",
        ["TIKTOK"] = "TikTok",
        ["LIVECHART"] = "LiveChart",
    };

    /// <summary>The sites whose IDs <see cref="Api.Sites"/> looks anime up by, keyed by the name it uses.</summary>
    private static readonly Dictionary<string, string> SiteEnums = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AniDB"] = "ANIDB",
        ["AniList"] = "ANILIST",
        ["MyAnimeList"] = "MAL",
        ["Kitsu"] = "KITSU",
    };

    /// <summary>
    /// Gets the GraphQL enum of a site the plugin looks anime up by.
    /// </summary>
    /// <param name="site">Site name as <see cref="Api.Sites"/> spells it, such as <c>AniList</c>.</param>
    /// <returns>The enum name, or null when animethemes.moe cannot hold IDs of that site.</returns>
    internal static string? SiteEnum(string? site)
        => site != null && SiteEnums.TryGetValue(site.Trim(), out var value) ? value : null;

    /// <summary>
    /// Gets a GraphQL site as the REST API spelled it.
    /// </summary>
    /// <param name="site">Enum name, such as <c>ANILIST</c>.</param>
    /// <returns>The site name, such as <c>AniList</c>.</returns>
    internal static string? SiteName(string? site)
        => site == null ? null : SiteNames.TryGetValue(site, out var name) ? name : Humanize(site);

    /// <summary>
    /// Reads one property of a JSON object, for the few members the client itself reads.
    /// </summary>
    /// <param name="element">Object, or null.</param>
    /// <param name="name">Property name as the GraphQL query asks for it.</param>
    /// <returns>The property, or null when it is absent or its parent is not an object.</returns>
    internal static JsonElement? Field(JsonElement? element, string name)
        => element is JsonElement value && value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found)
            ? found
            : null;

    /// <summary>
    /// Reads a list of anime, as a plain array or as the nodes of a connection.
    /// </summary>
    /// <param name="element">Anime array, connection, or null.</param>
    /// <returns>The anime that could be read.</returns>
    internal static IEnumerable<Anime> ReadAnimeList(JsonElement? element)
        => Items(Field(element, "nodes") ?? element)
            .Select(node => ReadAnime(node))
            .OfType<Anime>();

    /// <summary>
    /// Reads one anime. Missing relations stay null: a search result has no entries and a franchise
    /// member has no themes.
    /// </summary>
    /// <param name="element">The anime node.</param>
    /// <returns>The anime, or null when the element is not an object.</returns>
    internal static Anime? ReadAnime(JsonElement? element)
    {
        if (element is not JsonElement node || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = Field(node, "title");
        var slug = Text(Field(node, "slug")) ?? string.Empty;
        return new Anime(
            Number(Field(node, "id")) ?? 0,
            Text(Field(title, "romaji")) ?? Text(Field(title, "english")) ?? Text(Field(title, "native")) ?? slug,
            slug,
            Number(Field(node, "year")),
            ReadThemes(Field(node, "animethemes")),
            ReadResources(Field(node, "resources")))
        {
            Season = SeasonName(Text(Field(node, "season"))),
            MediaFormat = FormatName(Text(Field(node, "format"))),
            Images = ReadImages(Field(node, "images")),
            Synonyms = ReadSynonyms(Field(node, "synonyms")),
            Series = ReadSeries(Field(node, "series")),
        };
    }

    private static Collection<AnimeTheme>? ReadThemes(JsonElement? element)
    {
        var themes = Items(element).Select(ReadTheme).ToList();
        return themes.Count == 0 ? null : new Collection<AnimeTheme>(themes);
    }

    private static AnimeTheme ReadTheme(JsonElement node)
    {
        var song = Field(node, "song");
        return new AnimeTheme(
            Number(Field(node, "id")) ?? 0,
            ThemeTypeName(Text(Field(node, "type"))),
            Number(Field(node, "sequence")),
            Text(Field(node, "slug")),
            song is JsonElement value && value.ValueKind == JsonValueKind.Object ? ReadSong(value) : null,
            ReadEntries(Field(node, "animethemeentries")));
    }

    private static Song ReadSong(JsonElement node)
    {
        // A group performs through its members: one performance per member, all credited to the group,
        // while the REST API listed the group once. Billing order is the order of the performances.
        var artists = Items(Field(node, "performances"))
            .Select(performance => Field(performance, "artist"))
            .Select(artist => artist is JsonElement value && value.ValueKind == JsonValueKind.Object ? ReadArtist(value) : null)
            .OfType<Artist>()
            .DistinctBy(artist => artist.Id)
            .ToList();
        var title = Field(node, "title");
        return new Song(
            Number(Field(node, "id")) ?? 0,
            Text(Field(title, "romaji")) ?? Text(Field(title, "native")),
            artists.Count == 0 ? null : new Collection<Artist>(artists));
    }

    private static Artist ReadArtist(JsonElement node)
        => new(
            Number(Field(node, "id")) ?? 0,
            Text(Field(Field(node, "name"), "main")) ?? string.Empty,
            Text(Field(node, "slug")));

    private static Collection<AnimeThemeEntry>? ReadEntries(JsonElement? element)
    {
        var entries = Items(element).Select(ReadEntry).ToList();
        return entries.Count == 0 ? null : new Collection<AnimeThemeEntry>(entries);
    }

    private static AnimeThemeEntry ReadEntry(JsonElement node)
        => new(
            Number(Field(node, "id")) ?? 0,
            Number(Field(node, "version")),
            Text(Field(node, "episodes")),
            Flag(Field(node, "nsfw")),
            Flag(Field(node, "spoiler")),
            ReadVideos(Field(node, "videos")));

    private static Collection<Video>? ReadVideos(JsonElement? element)
    {
        var videos = Items(Field(element, "nodes")).Select(ReadVideo).ToList();
        return videos.Count == 0 ? null : new Collection<Video>(videos);
    }

    private static Video ReadVideo(JsonElement node)
    {
        var audio = Field(node, "audio");
        return new Video(
            Number(Field(node, "id")) ?? 0,
            Text(Field(node, "basename")),
            SourceName(Text(Field(node, "source"))),
            OverlapName(Text(Field(node, "overlap"))),
            Text(Field(node, "link")),
            Flag(Field(node, "nc")),
            audio is JsonElement value && value.ValueKind == JsonValueKind.Object ? ReadAudio(value) : null)
        {
            Resolution = Number(Field(node, "resolution")),
            Size = BigNumber(Field(node, "size")),
            Subbed = Flag(Field(node, "subbed")),
            Lyrics = Flag(Field(node, "lyrics")),
        };
    }

    private static Audio ReadAudio(JsonElement node)
        => new(Number(Field(node, "id")) ?? 0, Text(Field(node, "link")))
        {
            Size = BigNumber(Field(node, "size")),
        };

    private static Collection<AnimeImage>? ReadImages(JsonElement? element)
    {
        var images = Items(Field(element, "nodes"))
            .Select(node => new AnimeImage(FacetName(Text(Field(node, "facet"))), Text(Field(node, "link"))))
            .ToList();
        return images.Count == 0 ? null : new Collection<AnimeImage>(images);
    }

    private static Collection<AnimeSynonym>? ReadSynonyms(JsonElement? element)
    {
        var synonyms = Items(element)
            .Select(node => new AnimeSynonym(Text(Field(node, "text"))))
            .ToList();
        return synonyms.Count == 0 ? null : new Collection<AnimeSynonym>(synonyms);
    }

    private static Collection<AnimeSeries>? ReadSeries(JsonElement? element)
    {
        var series = Items(Field(element, "nodes"))
            .Select(node => new AnimeSeries(
                Number(Field(node, "id")) ?? 0,
                Text(Field(node, "name")),
                Text(Field(node, "slug"))))
            .ToList();
        return series.Count == 0 ? null : new Collection<AnimeSeries>(series);
    }

    private static Collection<Resource>? ReadResources(JsonElement? element)
    {
        var resources = Items(Field(element, "nodes"))
            .Select(node => new Resource(Number(Field(node, "externalId")), SiteName(Text(Field(node, "site")))))
            .ToList();
        return resources.Count == 0 ? null : new Collection<Resource>(resources);
    }

    private static string? SeasonName(string? season)
    {
        if (season == null)
        {
            return null;
        }

        return season.ToUpperInvariant() switch
        {
            "WINTER" => "Winter",
            "SPRING" => "Spring",
            "SUMMER" => "Summer",
            "FALL" => "Fall",
            _ => Humanize(season),
        };
    }

    private static string? FormatName(string? format)
    {
        if (format == null)
        {
            return null;
        }

        return format.ToUpperInvariant() switch
        {
            "TV" => "TV",
            "TV_SHORT" => "TV Short",
            "OVA" => "OVA",
            "ONA" => "ONA",
            "MOVIE" => "Movie",
            "SPECIAL" => "Special",
            _ => Humanize(format),
        };
    }

    private static string? FacetName(string? facet)
    {
        if (facet == null)
        {
            return null;
        }

        return facet.ToUpperInvariant() switch
        {
            "SMALL_COVER" => "Small Cover",
            "LARGE_COVER" => "Large Cover",
            "GRILL" => "Grill",
            "DOCUMENT" => "Document",
            "AVATAR" => "Avatar",
            "BANNER" => "Banner",
            _ => Humanize(facet),
        };
    }

    private static ThemeType ThemeTypeName(string? type) => type?.ToUpperInvariant() switch
    {
        "OP" => ThemeType.OP,
        "ED" => ThemeType.ED,
        _ => ThemeType.Other,
    };

    private static OverlapType OverlapName(string? overlap) => overlap?.ToUpperInvariant() switch
    {
        "TRANS" => OverlapType.Transition,
        "OVER" => OverlapType.Over,
        _ => OverlapType.None,
    };

    private static VideoSource SourceName(string? source)
        => source != null && Enum.TryParse<VideoSource>(source, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : VideoSource.Unknown;

    /// <summary>Reads a JSON array of objects; anything else is an empty sequence.</summary>
    private static IEnumerable<JsonElement> Items(JsonElement? element)
        => element is JsonElement value && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Text(JsonElement? element)
        => element is JsonElement value && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement? element)
        => element is JsonElement value && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? BigNumber(JsonElement? element)
        => element is JsonElement value && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static bool Flag(JsonElement? element)
        => element is JsonElement value && value.ValueKind == JsonValueKind.True;

    /// <summary>Turns an enum name this version does not know into a readable label.</summary>
    private static string Humanize(string name)
        => string.Join(
            ' ',
            name.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
}
