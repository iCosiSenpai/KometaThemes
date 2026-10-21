using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// The sites whose IDs animethemes.moe can look anime up by.
/// </summary>
/// <remarks>
/// animethemes.moe links anime to AniDB, AniList, MyAnimeList and Kitsu (and to sites Jellyfin has no
/// ID for). 2.0 also asked for aniSearch IDs, which animethemes.moe does not store: every such request
/// came back empty.
/// </remarks>
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

    /// <summary>
    /// Jellyfin provider ID names and the animethemes.moe site each one is looked up on.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ProviderToSite = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AniDB"] = AniDB,
        ["AniList"] = AniList,
        ["MyAnimeList"] = MyAnimeList,
        ["Kitsu"] = Kitsu,
    };

    /// <summary>
    /// Every supported provider ID, in the default order.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPriority = ["AniDB", "AniList", "MyAnimeList", "Kitsu"];

    /// <summary>
    /// Normalizes a provider order: known providers once each, in canonical spelling, missing ones appended.
    /// </summary>
    /// <param name="providers">The configured provider order.</param>
    /// <returns>A normalized order.</returns>
    public static Collection<string> NormalizeProviderPriority(IEnumerable<string>? providers)
    {
        var normalized = new List<string>();
        foreach (var provider in providers ?? [])
        {
            var canonical = DefaultPriority.FirstOrDefault(p => string.Equals(p, provider?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (canonical != null && !normalized.Contains(canonical, StringComparer.Ordinal))
            {
                normalized.Add(canonical);
            }
        }

        normalized.AddRange(DefaultPriority.Where(p => !normalized.Contains(p, StringComparer.Ordinal)));
        return new Collection<string>(normalized);
    }
}
