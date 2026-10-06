using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.KometaThemes.Models;

// Serialized state: the JSON serializer needs plain, settable lists.
#pragma warning disable CA1002, CA2227

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// A theme file KometaThemes wrote, as recorded next to it.
/// </summary>
/// <remarks>
/// Property names match the 1.x tracker, which stored a bare array of these, so old files load as they are.
/// </remarks>
public sealed class ThemeRecord
{
    /// <summary>Gets or sets the animethemes.moe theme ID; negative for imported themes.</summary>
    public int ThemeId { get; set; }

    /// <summary>Gets or sets opening or ending.</summary>
    public ThemeType Type { get; set; }

    /// <summary>Gets or sets the sequence number.</summary>
    public int Sequence { get; set; }

    /// <summary>Gets or sets the theme slug, or the YouTube video ID for imports.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Gets or sets the file name inside <see cref="Directory"/>.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the theme folder, <c>theme-music</c> or <c>backdrops</c>.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Gets or sets the season number the file was written for; 0 for the series or a movie.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets when the file was written.</summary>
    public DateTime DownloadedAt { get; set; }

    /// <summary>Gets or sets the library item the file belongs to.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets where the file came from.</summary>
    public ThemeSource Source { get; set; }

    /// <summary>Gets or sets the source page, for imports.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the animethemes.moe anime ID. Missing in 1.x records.</summary>
    public int? AnimeId { get; set; }

    /// <summary>Gets or sets the song title. Missing in 1.x records.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the credited artists. Missing in 1.x records.</summary>
    public string? Artists { get; set; }

    /// <summary>Gets or sets the volume baked into the file, in percent. Missing in 1.x records, whose name carries it.</summary>
    public int? Volume { get; set; }

    /// <summary>Gets the media type, derived from the folder (1.x records may lack it, so the extension decides).</summary>
    [JsonIgnore]
    public MediaType Media
        => string.Equals(EffectiveDirectory, ThemeFileKinds.VideoDirectory, StringComparison.Ordinal) ? MediaType.Video : MediaType.Audio;

    /// <summary>Gets the folder, inferred from the extension when a 1.x record lacks it.</summary>
    [JsonIgnore]
    public string EffectiveDirectory
        => ThemeFileKinds.IsThemeDirectory(Directory) ? Directory : ThemeFileKinds.DirectoryForFileName(FileName);

    /// <summary>Gets the volume in percent, from the record or from a 1.x file name.</summary>
    [JsonIgnore]
    public int? EffectiveVolume => Volume ?? ThemeNaming.LegacyVolume(FileName);
}

/// <summary>
/// The owner's choice for one theme in one folder: always keep it, or never download it.
/// </summary>
public sealed class ThemeOverride
{
    /// <summary>Gets or sets the animethemes.moe theme ID.</summary>
    public int ThemeId { get; set; }

    /// <summary>Gets or sets the media type the choice applies to.</summary>
    public MediaType Media { get; set; }

    /// <summary>Gets or sets a value indicating whether the theme is kept (true) or hidden (false).</summary>
    public bool Keep { get; set; }
}

/// <summary>
/// The state file KometaThemes keeps in each folder it writes themes to (<c>_kometa_themes.json</c>).
/// </summary>
public sealed class FolderState
{
    /// <summary>Format version written by this release.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Gets or sets the format version.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Gets or sets the item whose folder this is.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the anime entries the last check used.</summary>
    public List<int> AnimeIds { get; set; } = [];

    /// <summary>Gets or sets when the folder was last brought up to date.</summary>
    public DateTime? CheckedUtc { get; set; }

    /// <summary>Gets or sets the owner's per-theme choices.</summary>
    public List<ThemeOverride> Overrides { get; set; } = [];

    /// <summary>Gets or sets the files written here.</summary>
    public List<ThemeRecord> Records { get; set; } = [];

    /// <summary>
    /// Gets the override for a theme and media type.
    /// </summary>
    /// <param name="themeId">Theme ID.</param>
    /// <param name="media">Media type.</param>
    /// <returns>True to keep, false to hide, null to follow the settings.</returns>
    public bool? OverrideFor(int themeId, MediaType media)
        => Overrides.LastOrDefault(o => o.ThemeId == themeId && o.Media == media)?.Keep;

    /// <summary>
    /// Sets or clears an override.
    /// </summary>
    /// <param name="themeId">Theme ID.</param>
    /// <param name="media">Media type.</param>
    /// <param name="keep">True to keep, false to hide, null to follow the settings.</param>
    public void SetOverride(int themeId, MediaType media, bool? keep)
    {
        Overrides.RemoveAll(o => o.ThemeId == themeId && o.Media == media);
        if (keep.HasValue)
        {
            Overrides.Add(new ThemeOverride { ThemeId = themeId, Media = media, Keep = keep.Value });
        }
    }
}
