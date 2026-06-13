using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Constants for external site names used by the AnimeThemes API.
/// </summary>
public static class Sites
{
    /// <summary>AniDB site identifier.</summary>
    public const string AniDB = "AniDB";

    /// <summary>MyAnimeList site identifier.</summary>
    public const string MyAnimeList = "MyAnimeList";

    /// <summary>AniList site identifier.</summary>
    public const string AniList = "AniList";

    /// <summary>Kitsu site identifier.</summary>
    public const string Kitsu = "Kitsu";

    /// <summary>AniSearch site identifier (note lowercase 'ani').</summary>
    public const string AniSearch = "aniSearch";

    /// <summary>
    /// Mapping from Jellyfin provider ID names to AnimeThemes API site names.
    /// </summary>
    public static readonly Dictionary<string, string> ProviderToSite = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AniDB"] = AniDB,
        ["MyAnimeList"] = MyAnimeList,
        ["AniList"] = AniList,
        ["Kitsu"] = Kitsu,
        ["AniSearch"] = AniSearch,
    };

    /// <summary>
    /// All supported provider IDs in default priority order.
    /// </summary>
    public static readonly string[] DefaultPriority =
    [
        "AniDB",
        "AniList",
        "MyAnimeList",
        "Kitsu",
        "AniSearch"
    ];

    private static readonly Dictionary<string, string> CanonicalProviderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AniDB"] = "AniDB",
        ["AniList"] = "AniList",
        ["MyAnimeList"] = "MyAnimeList",
        ["Kitsu"] = "Kitsu",
        ["AniSearch"] = "AniSearch",
    };

    /// <summary>
    /// Normalizes a provider ID or site alias to the canonical Jellyfin provider ID.
    /// </summary>
    /// <param name="value">Provider ID or site alias.</param>
    /// <returns>The canonical provider ID, or null if unsupported.</returns>
    public static string? NormalizeProviderId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return CanonicalProviderIds.TryGetValue(value.Trim(), out var canonical)
            ? canonical
            : null;
    }

    /// <summary>
    /// Normalizes a provider priority list to supported provider IDs and appends missing defaults.
    /// </summary>
    /// <param name="providers">The configured provider order.</param>
    /// <returns>A normalized priority list.</returns>
    public static Collection<string> NormalizeProviderPriority(IEnumerable<string>? providers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();

        if (providers is not null)
        {
            foreach (var provider in providers)
            {
                var canonical = NormalizeProviderId(provider);
                if (canonical is not null && seen.Add(canonical))
                {
                    normalized.Add(canonical);
                }
            }
        }

        foreach (var provider in DefaultPriority.Where(seen.Add))
        {
            normalized.Add(provider);
        }

        return new Collection<string>(normalized);
    }
}
