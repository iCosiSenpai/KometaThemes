using System;
using Jellyfin.Plugin.KometaThemes.Caching;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Response DTO for cache statistics.
/// </summary>
/// <param name="PositiveEntries">Positive cache entries.</param>
/// <param name="NegativeEntries">Negative cache entries.</param>
/// <param name="TotalHits">Cache hits since startup.</param>
/// <param name="TotalMisses">Cache misses since startup.</param>
/// <param name="TotalEntries">Total cache entries.</param>
/// <param name="HitRatePercent">Hit rate percentage.</param>
public record CacheStatsResponse(
    int PositiveEntries,
    int NegativeEntries,
    long TotalHits,
    long TotalMisses,
    int TotalEntries,
    double HitRatePercent)
{
    /// <summary>
    /// Creates a DTO from the internal cache statistics.
    /// </summary>
    /// <param name="stats">Internal cache statistics.</param>
    /// <returns>A response DTO.</returns>
    public static CacheStatsResponse From(CacheStats stats)
    {
        var totalRequests = stats.TotalHits + stats.TotalMisses;
        var totalEntries = stats.PositiveEntries + stats.NegativeEntries;
        var hitRate = totalRequests == 0
            ? 0
            : Math.Round((double)stats.TotalHits / totalRequests * 100, 1);

        return new CacheStatsResponse(
            stats.PositiveEntries,
            stats.NegativeEntries,
            stats.TotalHits,
            stats.TotalMisses,
            totalEntries,
            hitRate);
    }
}
