using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Caching;

/// <summary>
/// Interface for the resolution cache that stores anime lookup results.
/// </summary>
public interface IResolutionCache
{
    /// <summary>
    /// Tries to get a cached result for the given key.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="result">Cached anime array if found (null for negative cache entries).</param>
    /// <returns>True if the key exists in cache and is not expired.</returns>
    bool TryGet(string key, out Anime[]? result);

    /// <summary>
    /// Stores a positive result in the cache.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="anime">The resolved anime.</param>
    void SetPositive(string key, Anime[] anime);

    /// <summary>
    /// Stores a negative result (no match found) in the cache to avoid re-querying.
    /// </summary>
    /// <param name="key">Cache key.</param>
    void SetNegative(string key);

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets statistics about the cache.
    /// </summary>
    /// <returns>Cache statistics.</returns>
    CacheStats GetStats();
}

/// <summary>
/// Statistics about the resolution cache.
/// </summary>
/// <param name="PositiveEntries">Number of positive (successful) cache entries.</param>
/// <param name="NegativeEntries">Number of negative (miss) cache entries.</param>
/// <param name="TotalHits">Total number of cache hits since startup.</param>
/// <param name="TotalMisses">Total number of cache misses since startup.</param>
public record CacheStats(int PositiveEntries, int NegativeEntries, long TotalHits, long TotalMisses);
