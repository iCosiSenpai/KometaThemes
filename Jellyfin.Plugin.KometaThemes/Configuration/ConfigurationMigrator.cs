using System;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Api;

namespace Jellyfin.Plugin.KometaThemes.Configuration;

/// <summary>
/// Brings a loaded configuration up to the current schema.
/// </summary>
public static class ConfigurationMigrator
{
    /// <summary>
    /// Upgrades a configuration in place.
    /// </summary>
    /// <param name="configuration">The loaded configuration.</param>
    /// <returns>Whether anything changed and the file should be saved.</returns>
    public static bool Migrate(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var before = Snapshot(configuration);
        var schemaBefore = configuration.SchemaVersion;

        if (configuration.SchemaVersion < PluginConfiguration.CurrentSchemaVersion)
        {
            // A 1.x file always carries the library pattern (it defaulted to "Anime"); a new install
            // has none. Upgrades keep working without the setup, which a new install goes through.
            var upgraded = configuration.LibraryPattern != null;
            if (upgraded)
            {
                configuration.SetupCompletedVersion = PluginConfiguration.CurrentSchemaVersion;
                if (string.IsNullOrWhiteSpace(configuration.LibraryPattern))
                {
                    configuration.LibraryPattern = LibraryPatternMatcher.DefaultPattern;
                }
            }

            configuration.SchemaVersion = PluginConfiguration.CurrentSchemaVersion;
        }

        var normalized = Sites.NormalizeProviderPriority(configuration.ProviderPriority);
        if (!configuration.ProviderPriority.SequenceEqual(normalized, StringComparer.Ordinal))
        {
            configuration.ProviderPriority = normalized;
        }

        // Duplicate entries for one item (1.x de-duplicated with plain string equality, so the same
        // GUID in two formats survived twice): keep the most recent.
        Dedupe(configuration.SkippedItems, entry => entry.ItemId, entry => entry.SkippedUtc);
        Dedupe(configuration.ManualBindings, entry => entry.ItemId, entry => entry.BoundAt);

        configuration.NormalizeBounds();
        configuration.TrimLists();

        return schemaBefore != configuration.SchemaVersion || !string.Equals(before, Snapshot(configuration), StringComparison.Ordinal);
    }

    private static void Dedupe<T>(System.Collections.ObjectModel.Collection<T> list, Func<T, string> key, Func<T, DateTime> stamp)
    {
        var keep = list
            .GroupBy(entry => Guid.TryParse(key(entry), out var id) ? id.ToString("N") : (key(entry) ?? string.Empty).Trim().ToUpperInvariant())
            .Where(group => group.Key.Length > 0)
            .Select(group => group.OrderByDescending(stamp).First())
            .ToList();
        if (keep.Count == list.Count)
        {
            return;
        }

        list.Clear();
        foreach (var entry in keep)
        {
            list.Add(entry);
        }
    }

    private static string Snapshot(PluginConfiguration c)
        => string.Join(
            '|',
            c.SetupCompletedVersion,
            c.LibraryPattern,
            string.Join(',', c.ProviderPriority),
            c.SkippedItems.Count,
            c.ManualBindings.Count,
            c.MaxThemesPerSeason,
            c.DegreeOfParallelism,
            c.DownloadTimeoutSeconds,
            c.RateLimitPerMinute,
            c.PositiveCacheTtlDays,
            c.NegativeCacheTtlHours,
            c.TitleMatchThreshold,
            c.AudioSettings?.FetchType,
            c.AudioSettings?.Volume,
            c.VideoSettings?.FetchType,
            c.VideoSettings?.Volume);
}
