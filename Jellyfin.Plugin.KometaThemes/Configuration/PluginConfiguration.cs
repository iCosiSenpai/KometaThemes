using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.KometaThemes.Configuration;

/// <summary>
/// Plugin configuration, stored by Jellyfin as XML.
/// </summary>
/// <remarks>
/// <para>
/// Never pre-populate a collection in the constructor: <c>XmlSerializer</c> appends to an existing
/// collection instead of replacing it, which is how 1.0 grew its provider list by five entries on every
/// load. Defaults for collections are applied by <see cref="ConfigurationMigrator"/> after loading.
/// </para>
/// <para>
/// Settings properties keep their 1.x names so an upgrade carries them over. Properties that 2.0
/// dropped are simply no longer declared; the serializer ignores their elements in old files.
/// </para>
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Schema version written by this release.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Largest number of entries kept in the exclusion and match lists.</summary>
    public const int MaxPersistedListEntries = 2000;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        AudioSettings = new MediaTypeConfiguration
        {
            FetchType = FetchType.All,
            IgnoreOverlapping = true,
            Volume = 0.5,
        };

        VideoSettings = new MediaTypeConfiguration
        {
            FetchType = FetchType.None,
            IgnoreOverlapping = true,
            IgnoreThemesWithCredits = true,
            Volume = 0.5,
        };

        MaxThemesPerSeason = 5;
        PerSeasonThemes = true;
        AutoSyncOnItemAdded = true;
        EnableTitleFallback = true;
        TitleMatchThreshold = 0.80;
        RateLimitPerMinute = 60;
        PositiveCacheTtlDays = 7;
        NegativeCacheTtlHours = 24;
        DownloadTimeoutSeconds = 120;
        DegreeOfParallelism = 2;
    }

    /// <summary>Gets or sets the schema version of this file. 0 means it was written by 1.x or is new.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets or sets the schema version whose first-run setup the owner has completed. 0 shows the setup.</summary>
    public int SetupCompletedVersion { get; set; }

    /// <summary>Gets or sets a value indicating whether the "what's new in 2.0" note was dismissed.</summary>
    public bool WhatsNewDismissed { get; set; }

    /// <summary>Gets the IDs of the libraries KometaThemes manages.</summary>
    public Collection<string> LibraryIds { get; } = new();

    /// <summary>
    /// Gets or sets the 1.x library name pattern. Only read once, to pick the libraries of an upgraded
    /// install; cleared after that.
    /// </summary>
    public string? LibraryPattern { get; set; }

    /// <summary>Gets or sets what to download as theme songs.</summary>
    public MediaTypeConfiguration AudioSettings { get; set; }

    /// <summary>Gets or sets what to download as theme videos.</summary>
    public MediaTypeConfiguration VideoSettings { get; set; }

    /// <summary>Gets or sets the most themes of each media type written to one series, season or movie.</summary>
    public int MaxThemesPerSeason { get; set; }

    /// <summary>Gets or sets a value indicating whether each season gets the themes of its own anime entry.</summary>
    public bool PerSeasonThemes { get; set; }

    /// <summary>Gets or sets a value indicating whether new library items are processed as they arrive.</summary>
    public bool AutoSyncOnItemAdded { get; set; }

    /// <summary>Gets or sets a value indicating whether a removed item's downloaded themes are deleted.</summary>
    public bool CleanupThemesOnItemRemoved { get; set; }

#pragma warning disable CA2227 // XmlSerializer needs the setter; the list is replaced, never appended to, by the migrator.
    /// <summary>Gets or sets the order in which external IDs are tried.</summary>
    public Collection<string> ProviderPriority { get; set; } = new();
#pragma warning restore CA2227

    /// <summary>Gets or sets a value indicating whether items without a usable ID are matched by title.</summary>
    public bool EnableTitleFallback { get; set; }

    /// <summary>Gets or sets how confident a title match must be, 0.5 to 1.</summary>
    public double TitleMatchThreshold { get; set; }

    /// <summary>Gets or sets the request budget for animethemes.moe per minute.</summary>
    public int RateLimitPerMinute { get; set; }

    /// <summary>Gets or sets how long a found match is reused, in days.</summary>
    public int PositiveCacheTtlDays { get; set; }

    /// <summary>Gets or sets how long a failed lookup is remembered, in hours.</summary>
    public int NegativeCacheTtlHours { get; set; }

    /// <summary>Gets or sets the time limit of one ffmpeg conversion, in seconds.</summary>
    public int DownloadTimeoutSeconds { get; set; }

    /// <summary>Gets or sets how many theme files of one item are fetched at the same time.</summary>
    public int DegreeOfParallelism { get; set; }

    /// <summary>Gets or sets a value indicating whether themes can be imported from YouTube links.</summary>
    public bool EnableYouTubeImport { get; set; }

    /// <summary>Gets or sets an explicit yt-dlp path. Empty uses yt-dlp when found, else the bundled extractor.</summary>
    public string? YtDlpPath { get; set; }

    /// <summary>Gets or sets when the last full check finished.</summary>
    public DateTime? LastFullSyncUtc { get; set; }

    /// <summary>Gets or sets the one-line result of the last full check.</summary>
    public string? LastSyncSummary { get; set; }

    /// <summary>Gets the items the owner excluded.</summary>
    public Collection<SkippedItemEntry> SkippedItems { get; } = new();

    /// <summary>Gets the owner's matches of series, seasons and movies to anime entries.</summary>
    public Collection<ManualBindingEntry> ManualBindings { get; } = new();

    /// <summary>
    /// Finds the exclusion entry of an item.
    /// </summary>
    /// <param name="itemId">Item ID.</param>
    /// <returns>The entry, or null.</returns>
    public SkippedItemEntry? FindSkipped(Guid itemId)
        => SkippedItems.LastOrDefault(entry => IdEquals(entry.ItemId, itemId));

    /// <summary>
    /// Finds the match the owner set for an item.
    /// </summary>
    /// <param name="itemId">Item ID.</param>
    /// <returns>The entry, or null.</returns>
    public ManualBindingEntry? FindBinding(Guid itemId)
        => ManualBindings.LastOrDefault(entry => IdEquals(entry.ItemId, itemId));

    /// <summary>
    /// Gets the excluded item IDs.
    /// </summary>
    /// <returns>A set of IDs.</returns>
    public HashSet<Guid> GetSkippedIds()
        => SkippedItems.Select(entry => Guid.TryParse(entry.ItemId, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();

    /// <summary>
    /// Clamps every value to its documented range.
    /// </summary>
    public void NormalizeBounds()
    {
        AudioSettings ??= new MediaTypeConfiguration { FetchType = FetchType.All, IgnoreOverlapping = true, Volume = 0.5 };
        VideoSettings ??= new MediaTypeConfiguration { FetchType = FetchType.None, IgnoreOverlapping = true, IgnoreThemesWithCredits = true, Volume = 0.5 };
        foreach (var settings in new[] { AudioSettings, VideoSettings })
        {
            settings.Volume = Math.Clamp(double.IsFinite(settings.Volume) ? settings.Volume : 0.5, 0.0, 1.0);
            if (settings.FetchType == FetchType.AllPerSeason)
            {
                settings.FetchType = FetchType.All;
            }
        }

        MaxThemesPerSeason = Math.Clamp(MaxThemesPerSeason, 1, 50);
        DegreeOfParallelism = Math.Clamp(DegreeOfParallelism, 1, 4);
        DownloadTimeoutSeconds = Math.Clamp(DownloadTimeoutSeconds, 15, 600);
        RateLimitPerMinute = Math.Clamp(RateLimitPerMinute, Http.ApiThrottle.MinRatePerMinute, Http.ApiThrottle.MaxRatePerMinute);
        PositiveCacheTtlDays = Math.Clamp(PositiveCacheTtlDays, 1, 365);
        NegativeCacheTtlHours = Math.Clamp(NegativeCacheTtlHours, 1, 24 * 30);
        TitleMatchThreshold = Math.Clamp(double.IsFinite(TitleMatchThreshold) ? TitleMatchThreshold : 0.8, 0.5, 1.0);
    }

    /// <summary>
    /// Drops the oldest entries of the persisted lists once they pass <see cref="MaxPersistedListEntries"/>.
    /// </summary>
    public void TrimLists()
    {
        Trim(SkippedItems, entry => entry.SkippedUtc);
        Trim(ManualBindings, entry => entry.BoundAt);
    }

    private static void Trim<T>(Collection<T> list, Func<T, DateTime> stamp)
    {
        if (list.Count <= MaxPersistedListEntries)
        {
            return;
        }

        var keep = list.OrderByDescending(stamp).Take(MaxPersistedListEntries).ToList();
        list.Clear();
        foreach (var entry in keep)
        {
            list.Add(entry);
        }
    }

    private static bool IdEquals(string? stored, Guid itemId)
        => Guid.TryParse(stored, out var parsed) && parsed == itemId;
}
