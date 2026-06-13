using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.KometaThemes.Caching;

/// <summary>
/// Public information about a resolution cache entry.
/// </summary>
/// <param name="Key">Raw cache key.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="Kind">Entry group for dashboard display.</param>
/// <param name="Reason">Reason for negative entries.</param>
/// <param name="Timestamp">UTC creation time.</param>
/// <param name="AnimeNames">Resolved anime names for positive entries.</param>
public record CacheEntryInfo(
    string Key,
    string DisplayName,
    string Kind,
    string Reason,
    DateTime Timestamp,
    IReadOnlyList<string> AnimeNames);
