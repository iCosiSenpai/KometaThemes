using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Caching;

/// <summary>
/// Remembers lookups against animethemes.moe and AniList between checks.
/// </summary>
public interface IResolutionCache
{
    /// <summary>
    /// Looks up anime stored under a key.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="result">The anime, or null for a remembered "not found".</param>
    /// <returns>Whether the key was cached and fresh.</returns>
    bool TryGet(string key, out Anime[]? result);

    /// <summary>Stores anime found for a key.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="anime">The anime.</param>
    void SetPositive(string key, Anime[] anime);

    /// <summary>Remembers that a key found nothing.</summary>
    /// <param name="key">Cache key.</param>
    void SetNegative(string key);

    /// <summary>
    /// Looks up a text value, such as a serialized AniList chain.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="value">The value.</param>
    /// <returns>Whether the key was cached and fresh.</returns>
    bool TryGetText(string key, out string? value);

    /// <summary>Stores a text value.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="value">The value.</param>
    void SetText(string key, string value);

    /// <summary>Forgets one key.</summary>
    /// <param name="key">Cache key.</param>
    void Remove(string key);

    /// <summary>Forgets everything.</summary>
    void Clear();

    /// <summary>Gets entry counts and hit statistics.</summary>
    /// <returns>The statistics.</returns>
    CacheStats GetStats();
}

/// <summary>
/// Cache statistics.
/// </summary>
/// <param name="PositiveEntries">Remembered matches.</param>
/// <param name="NegativeEntries">Remembered misses.</param>
/// <param name="TotalHits">Lookups answered from the cache.</param>
/// <param name="TotalMisses">Lookups that went to the network.</param>
public record CacheStats(int PositiveEntries, int NegativeEntries, long TotalHits, long TotalMisses);
