using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

// Request bodies are bound by the serializer, which needs settable lists.
#pragma warning disable CA1002, CA2227

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>Settings of one media type, as the page edits them.</summary>
public sealed class MediaSettingsDto
{
    /// <summary>Gets or sets a value indicating whether this media type is downloaded.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many: <c>one</c> or <c>all</c>.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = "all";

    /// <summary>Gets or sets a value indicating whether openings are included.</summary>
    [JsonPropertyName("openings")]
    public bool Openings { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether endings are included.</summary>
    [JsonPropertyName("endings")]
    public bool Endings { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether versions with episode content over the theme are skipped.</summary>
    [JsonPropertyName("skipOverlaps")]
    public bool SkipOverlaps { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether only creditless versions are accepted.</summary>
    [JsonPropertyName("creditlessOnly")]
    public bool CreditlessOnly { get; set; }

    /// <summary>Gets or sets the volume, 0 to 100.</summary>
    [JsonPropertyName("volume")]
    public int Volume { get; set; } = 50;
}

/// <summary>Every setting the page can change.</summary>
public sealed class SettingsDto
{
    /// <summary>Gets or sets the managed library IDs.</summary>
    [JsonPropertyName("libraryIds")]
    public List<string> LibraryIds { get; set; } = [];

    /// <summary>Gets or sets the theme song settings.</summary>
    [JsonPropertyName("audio")]
    public MediaSettingsDto Audio { get; set; } = new();

    /// <summary>Gets or sets the theme video settings.</summary>
    [JsonPropertyName("video")]
    public MediaSettingsDto Video { get; set; } = new();

    /// <summary>Gets or sets the most themes of each media type per folder.</summary>
    [JsonPropertyName("maxPerFolder")]
    public int MaxPerFolder { get; set; } = 5;

    /// <summary>Gets or sets a value indicating whether seasons get their own themes.</summary>
    [JsonPropertyName("perSeason")]
    public bool PerSeason { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether new anime are processed as they arrive.</summary>
    [JsonPropertyName("autoOnAdd")]
    public bool AutoOnAdd { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a removed anime's themes are deleted.</summary>
    [JsonPropertyName("cleanupOnRemove")]
    public bool CleanupOnRemove { get; set; }

    /// <summary>Gets or sets a value indicating whether anime without a usable ID are matched by title.</summary>
    [JsonPropertyName("titleFallback")]
    public bool TitleFallback { get; set; } = true;

    /// <summary>Gets or sets the title match confidence, 50 to 100.</summary>
    [JsonPropertyName("titleThreshold")]
    public int TitleThreshold { get; set; } = 80;

    /// <summary>Gets or sets the order of external IDs.</summary>
    [JsonPropertyName("providerPriority")]
    public List<string> ProviderPriority { get; set; } = [];

    /// <summary>Gets or sets the request budget per minute.</summary>
    [JsonPropertyName("rateLimit")]
    public int RateLimit { get; set; } = 60;

    /// <summary>Gets or sets how long matches are reused, in days.</summary>
    [JsonPropertyName("matchCacheDays")]
    public int MatchCacheDays { get; set; } = 7;

    /// <summary>Gets or sets how long misses are remembered, in hours.</summary>
    [JsonPropertyName("missCacheHours")]
    public int MissCacheHours { get; set; } = 24;

    /// <summary>Gets or sets the time limit of one conversion, in seconds.</summary>
    [JsonPropertyName("convertTimeout")]
    public int ConvertTimeout { get; set; } = 120;

    /// <summary>Gets or sets parallel downloads per anime.</summary>
    [JsonPropertyName("parallel")]
    public int Parallel { get; set; } = 2;

    /// <summary>Gets or sets a value indicating whether YouTube import is on.</summary>
    [JsonPropertyName("youTube")]
    public bool YouTube { get; set; }

    /// <summary>Gets or sets an explicit yt-dlp path.</summary>
    [JsonPropertyName("ytDlpPath")]
    public string? YtDlpPath { get; set; }
}

/// <summary>Request to match an item to an anime entry.</summary>
public sealed class MatchRequest
{
    /// <summary>Gets or sets the animethemes.moe anime ID.</summary>
    [JsonPropertyName("animeId")]
    public int AnimeId { get; set; }
}

/// <summary>Request to change which files of one theme a folder keeps.</summary>
public sealed class ThemeChoiceRequest
{
    /// <summary>Gets or sets the song choice: true keep, false never, null follow the settings.</summary>
    [JsonPropertyName("audio")]
    public bool? Audio { get; set; }

    /// <summary>Gets or sets the video choice: true keep, false never, null follow the settings.</summary>
    [JsonPropertyName("video")]
    public bool? Video { get; set; }

    /// <summary>Gets or sets which of the two to change: <c>audio</c>, <c>video</c> or <c>both</c>.</summary>
    [JsonPropertyName("change")]
    public string Change { get; set; } = "both";
}

/// <summary>Request to import a theme from YouTube.</summary>
public sealed class YouTubeRequest
{
    /// <summary>Gets or sets the YouTube link.</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Gets or sets <c>OP</c> or <c>ED</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "OP";

    /// <summary>Gets or sets the number, 1 to 99.</summary>
    [JsonPropertyName("sequence")]
    public int Sequence { get; set; } = 1;

    /// <summary>Gets or sets the song title; empty uses the video title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets <c>audio</c>, <c>video</c> or <c>both</c>.</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "audio";
}

/// <summary>Request to start a full check.</summary>
public sealed class CheckRequest
{
    /// <summary>Gets or sets a value indicating whether anime waiting after a problem are retried too.</summary>
    [JsonPropertyName("retryProblems")]
    public bool RetryProblems { get; set; }
}

/// <summary>An anime entry as the page shows it.</summary>
/// <param name="Id">animethemes.moe ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Slug">Slug.</param>
/// <param name="Year">Year.</param>
/// <param name="Season">Premiere season.</param>
/// <param name="Format">Format.</param>
/// <param name="Cover">Cover URL.</param>
/// <param name="Link">Page on animethemes.moe.</param>
/// <param name="Themes">Number of openings and endings.</param>
/// <param name="Score">Match score against the item, 0 to 100, when ranked.</param>
public sealed record AnimeDto(int Id, string Name, string Slug, int? Year, string? Season, string? Format, string? Cover, string Link, int Themes, int? Score);

/// <summary>A theme with what the folder holds of it.</summary>
/// <param name="ThemeId">Theme ID.</param>
/// <param name="AnimeId">Anime ID.</param>
/// <param name="Code">Code such as <c>OP1</c>.</param>
/// <param name="Type"><c>OP</c> or <c>ED</c>.</param>
/// <param name="Title">Song title.</param>
/// <param name="Artists">Artists.</param>
/// <param name="Episodes">Episodes as listed.</param>
/// <param name="Ranges">Episodes as start-end pairs.</param>
/// <param name="Audio">Song state.</param>
/// <param name="Video">Video state.</param>
/// <param name="Creditless">Whether the version is creditless.</param>
/// <param name="Overlap">Overlap with episode content.</param>
/// <param name="Source">Rip source.</param>
/// <param name="Resolution">Vertical resolution.</param>
/// <param name="Spoiler">Whether the version spoils the story.</param>
/// <param name="Nsfw">Whether the version is flagged not safe for work.</param>
public sealed record ThemeDto(
    int ThemeId,
    int AnimeId,
    string Code,
    string Type,
    string? Title,
    string Artists,
    string? Episodes,
    IReadOnlyList<int[]> Ranges,
    MediaStateDto Audio,
    MediaStateDto Video,
    bool Creditless,
    string Overlap,
    string Source,
    int? Resolution,
    bool Spoiler,
    bool Nsfw);

/// <summary>One media type of a theme in a folder.</summary>
/// <param name="Wanted">Whether the file should be in the folder (settings plus the owner's choice).</param>
/// <param name="BySettings">Whether the settings alone pick it.</param>
/// <param name="Choice">The owner's choice: true keep, false never, null follow the settings.</param>
/// <param name="File">The file present for it, if any.</param>
/// <param name="Preview">Streaming link on animethemes.moe.</param>
public sealed record MediaStateDto(bool Wanted, bool BySettings, bool? Choice, string? File, string? Preview);

/// <summary>A file in a theme folder.</summary>
/// <param name="Directory"><c>theme-music</c> or <c>backdrops</c>.</param>
/// <param name="FileName">File name.</param>
/// <param name="Source"><c>animethemes</c>, <c>youtube</c> or <c>yours</c> (not written by KometaThemes).</param>
/// <param name="Title">Song title, if recorded.</param>
/// <param name="Artists">Artists, if recorded.</param>
/// <param name="Code">Theme code, if recorded.</param>
/// <param name="Size">Size in bytes.</param>
public sealed record FileDto(string Directory, string FileName, string Source, string? Title, string? Artists, string? Code, long Size);

/// <summary>How a folder is matched.</summary>
/// <param name="State"><c>matched</c>, <c>inherit</c>, <c>notFound</c> or <c>unavailable</c>.</param>
/// <param name="Method">How, in words.</param>
/// <param name="Manual">Whether the owner chose it.</param>
/// <param name="Anime">Matched entries.</param>
public sealed record MatchDto(string State, string Method, bool Manual, IReadOnlyList<AnimeDto> Anime);

/// <summary>One folder of an item: the series or movie itself, or a season.</summary>
/// <param name="ItemId">ID of the series, season or movie.</param>
/// <param name="Label">Display label.</param>
/// <param name="SeasonNumber">Season number, 0 for the series or movie.</param>
/// <param name="Episodes">Episodes in the library.</param>
/// <param name="Year">Premiere year.</param>
/// <param name="Match">How it is matched.</param>
/// <param name="Themes">Themes of the matched entries.</param>
/// <param name="Files">Files in its theme folders.</param>
/// <param name="CheckedUtc">When it was last brought up to date.</param>
public sealed record FolderDto(Guid ItemId, string Label, int SeasonNumber, int Episodes, int? Year, MatchDto Match, IReadOnlyList<ThemeDto> Themes, IReadOnlyList<FileDto> Files, DateTime? CheckedUtc);

/// <summary>Everything the anime page shows.</summary>
/// <param name="Id">Item ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Year">Year.</param>
/// <param name="Type"><c>Series</c> or <c>Movie</c>.</param>
/// <param name="Summary">The library-list summary.</param>
/// <param name="Problem">The current problem, if any.</param>
/// <param name="Folders">Its folders.</param>
/// <param name="Note">Why themes cannot be written, if so.</param>
public sealed record ItemDetailDto(Guid Id, string Name, int? Year, string Type, Library.ItemSummary Summary, ProblemDto? Problem, IReadOnlyList<FolderDto> Folders, string? Note);

/// <summary>An item's problem.</summary>
/// <param name="Reason"><c>Unresolved</c> or <c>DownloadFailed</c>.</param>
/// <param name="Error">Explanation.</param>
/// <param name="Attempts">Failures in a row.</param>
/// <param name="NextAttemptUtc">Next automatic attempt.</param>
public sealed record ProblemDto(string Reason, string? Error, int Attempts, DateTime NextAttemptUtc);
