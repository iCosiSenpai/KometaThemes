using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Model.Plugins;

#pragma warning disable CA2227, CS1591

namespace Jellyfin.Plugin.KometaThemes.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        DegreeOfParallelism = 1;
        ForceSync = false;

        AudioSettings = new MediaTypeConfiguration()
        {
            FetchType = FetchType.Single,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = false,
            IgnoreOPs = false,
            IgnoreEDs = false,
            Volume = 0.5,
        };

        VideoSettings = new MediaTypeConfiguration()
        {
            FetchType = FetchType.Single,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = true,
            IgnoreOPs = false,
            IgnoreEDs = false,
            Volume = 0.5,
        };

        MovieSettings = new CollectionTypeConfiguration();

        // KometaThemes extensions
        UiLanguage = "en";
        ProviderPriority = Sites.NormalizeProviderPriority(null);
        EnableTitleFallback = true;
        TitleMatchThreshold = 0.80;
        RateLimitPerMinute = 60;
        PositiveCacheTtlDays = 7;
        NegativeCacheTtlHours = 24;

        // Library filtering
        LibraryPattern = "Anime";

        // Sync scheduling
        SyncIntervalHours = 6;

        // Dry-run mode
        DryRunMode = false;

        // Download settings
        DownloadTimeoutSeconds = 60;

        // Multi-season / multi-theme settings
        SeasonDetectionMode = "Auto";
        MaxThemesPerSeason = 5;

        // Playlist
        EnablePlaylist = false;
        PlaylistName = "Anime Themes";

        // Fallback mode
        MissingThemeFallbackMode = MissingThemeFallbackMode.None;

        VideoVolumeDefaultMigrated = false;
    }

    /// <summary>
    /// Gets or sets the degree of parallelism.
    /// </summary>
    public int DegreeOfParallelism { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sync should enforce conformity.
    /// </summary>
    public bool ForceSync { get; set; }

    /// <summary>
    /// Gets or sets the audio settings.
    /// </summary>
    public MediaTypeConfiguration AudioSettings { get; set; }

    /// <summary>
    /// Gets or sets the video settings.
    /// </summary>
    public MediaTypeConfiguration VideoSettings { get; set; }

    /// <summary>
    /// Gets or sets the download settings for the movie type.
    /// </summary>
    public CollectionTypeConfiguration MovieSettings { get; set; }

    // ── KometaThemes Extensions ──

    /// <summary>
    /// Gets or sets the plugin UI language.
    /// </summary>
    public string UiLanguage { get; set; }

#pragma warning disable CA2227
    /// <summary>
    /// Gets or sets the provider priority order for external ID resolution.
    /// </summary>
    public Collection<string> ProviderPriority { get; set; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets a value indicating whether to enable title-based fallback search.
    /// </summary>
    public bool EnableTitleFallback { get; set; }

    /// <summary>
    /// Gets or sets the minimum Levenshtein similarity threshold for title matching (0.0-1.0).
    /// </summary>
    public double TitleMatchThreshold { get; set; }

    /// <summary>
    /// Gets or sets the rate limit in requests per minute for the AnimeThemes API.
    /// </summary>
    public int RateLimitPerMinute { get; set; }

    /// <summary>
    /// Gets or sets the TTL in days for positive cache entries.
    /// </summary>
    public int PositiveCacheTtlDays { get; set; }

    /// <summary>
    /// Gets or sets the TTL in hours for negative cache entries.
    /// </summary>
    public int NegativeCacheTtlHours { get; set; }

    /// <summary>
    /// Gets or sets the library name pattern for selecting which libraries to sync.
    /// Only libraries whose name contains this pattern are included. Default: "Anime".
    /// </summary>
    public string LibraryPattern { get; set; }

    /// <summary>
    /// Gets or sets the sync interval in hours. Default: 6, range: 1-168.
    /// </summary>
    public int SyncIntervalHours { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether dry-run mode is enabled.
    /// When enabled, themes are resolved and logged but not downloaded.
    /// </summary>
    public bool DryRunMode { get; set; }

    /// <summary>
    /// Gets or sets the ffmpeg download timeout in seconds. Default: 60, range: 15-300.
    /// </summary>
    public int DownloadTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the season detection mode (ByName, ByEpisodeRange, Auto).
    /// </summary>
    public string SeasonDetectionMode { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of themes to download per season.
    /// </summary>
    public int MaxThemesPerSeason { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically create/update a global playlist.
    /// </summary>
    public bool EnablePlaylist { get; set; }

    /// <summary>
    /// Gets or sets the name of the global playlist.
    /// </summary>
    public string PlaylistName { get; set; }

    /// <summary>
    /// Gets or sets the library root path used to enumerate theme files for M3U export.
    /// </summary>
    public string? PlaylistExportRoot { get; set; }

    /// <summary>
    /// Gets or sets the fallback download mode when an item has no existing themes.
    /// </summary>
    public MissingThemeFallbackMode MissingThemeFallbackMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the old muted video default has been migrated.
    /// </summary>
    public bool VideoVolumeDefaultMigrated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the new item auto-sync is enabled.
    /// </summary>
    public bool AutoSyncOnItemAdded { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether orphaned theme files should be removed when an item is deleted from the library.
    /// </summary>
    public bool CleanupThemesOnItemRemoved { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to send an admin notification when a sync downloads new themes.
    /// </summary>
    public bool NotifyOnSyncComplete { get; set; }

    /// <summary>
    /// Gets or sets the last successful full-sync timestamp (UTC).
    /// </summary>
    public DateTime? LastFullSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the last sync summary (e.g. "123 items, 87 downloaded, 2 failed").
    /// </summary>
    public string? LastSyncSummary { get; set; }

    /// <summary>
    /// Gets the list of permanently skipped items.
    /// </summary>
#pragma warning disable CA2227
    public Collection<SkippedItemEntry> SkippedItems { get; } = new();
#pragma warning restore CA2227

    /// <summary>
    /// Gets a dictionary of skipped item IDs for fast lookup.
    /// </summary>
    /// <returns>Dictionary keyed by item ID.</returns>
    public Dictionary<string, SkippedItemEntry> GetSkippedItemsDictionary()
    {
        return SkippedItems.ToDictionary(i => i.ItemId, i => i);
    }
}
