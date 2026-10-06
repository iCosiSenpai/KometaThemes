using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Finds the animethemes.moe entry of series and movies: the owner's match first, then external IDs
/// in the configured order, then the title.
/// </summary>
public sealed class AnimeResolver
{
    private readonly AnimeThemesClient _client;
    private readonly IResolutionCache _cache;
    private readonly TitleSearchResolver _titles;
    private readonly ILogger<AnimeResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeResolver"/> class.
    /// </summary>
    /// <param name="client">animethemes.moe client.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="titles">Title search.</param>
    /// <param name="logger">Logger.</param>
    public AnimeResolver(AnimeThemesClient client, IResolutionCache cache, TitleSearchResolver titles, ILogger<AnimeResolver> logger)
    {
        _client = client;
        _cache = cache;
        _titles = titles;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a batch of series and movies.
    /// </summary>
    /// <param name="items">Items to resolve.</param>
    /// <param name="config">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A resolution for every item.</returns>
    public async Task<Dictionary<Guid, Resolution>> ResolveAsync(IReadOnlyList<BaseItem> items, PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(config);

        var results = new Dictionary<Guid, Resolution>();
        var unavailable = (string?)null;

        foreach (var item in items)
        {
            var binding = config.FindBinding(item.Id);
            if (binding == null)
            {
                continue;
            }

            try
            {
                var anime = await GetAnimeAsync(binding.AnimeId, cancellationToken).ConfigureAwait(false);
                if (anime != null)
                {
                    results[item.Id] = Resolution.Found([anime], "matched by you", manual: true);
                }
                else
                {
                    _logger.LogWarning("{Name}: the entry you chose ({AnimeId}) is no longer on animethemes.moe; trying automatic matching", item.Name, binding.AnimeId);
                }
            }
            catch (HttpRequestException ex)
            {
                unavailable = ex.Message;
                results[item.Id] = Resolution.Without(MatchState.Unavailable, "animethemes.moe could not be reached");
            }
        }

        foreach (var providerKey in Sites.NormalizeProviderPriority(config.ProviderPriority))
        {
            if (!Sites.ProviderToSite.TryGetValue(providerKey, out var site))
            {
                continue;
            }

            var pending = items.Where(item => !results.ContainsKey(item.Id)).ToList();
            if (pending.Count == 0)
            {
                break;
            }

            var byId = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in pending)
            {
                if (!item.TryGetProviderId(providerKey, out var externalId) || string.IsNullOrWhiteSpace(externalId))
                {
                    continue;
                }

                externalId = externalId.Trim();
                if (_cache.TryGet(CacheKey(site, externalId), out var cached))
                {
                    if (cached is { Length: > 0 })
                    {
                        results[item.Id] = Resolution.Found([Choose(item, cached)], Describe(providerKey, externalId));
                    }

                    continue;
                }

                if (!byId.TryGetValue(externalId, out var list))
                {
                    byId[externalId] = list = [];
                }

                list.Add(item);
            }

            if (byId.Count == 0 || unavailable != null)
            {
                continue;
            }

            Dictionary<string, Anime[]> found;
            try
            {
                found = await _client.FindByExternalIdsAsync(site, byId.Keys, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "animethemes.moe lookup by {Site} failed", site);
                unavailable = ex.Message;
                continue;
            }

            foreach (var (externalId, list) in byId)
            {
                var usable = found.TryGetValue(externalId, out var animes)
                    ? animes.Where(AnimeThemeAvailability.HasUsableTheme).ToArray()
                    : [];

                if (usable.Length == 0)
                {
                    _cache.SetNegative(CacheKey(site, externalId));
                    continue;
                }

                _cache.SetPositive(CacheKey(site, externalId), usable);
                foreach (var item in list)
                {
                    results[item.Id] = Resolution.Found([Choose(item, usable)], Describe(providerKey, externalId));
                }
            }
        }

        if (config.EnableTitleFallback)
        {
            foreach (var item in items.Where(item => !results.ContainsKey(item.Id)))
            {
                if (unavailable != null)
                {
                    break;
                }

                try
                {
                    var match = await _titles.ResolveAsync(item, config.TitleMatchThreshold, cancellationToken).ConfigureAwait(false);
                    if (match is { } m)
                    {
                        results[item.Id] = Resolution.Found([m.Anime], m.Detail);
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "animethemes.moe search failed for {Name}", item.Name);
                    unavailable = ex.Message;
                }
            }
        }

        foreach (var item in items)
        {
            if (!results.ContainsKey(item.Id))
            {
                results[item.Id] = unavailable != null
                    ? Resolution.Without(MatchState.Unavailable, "animethemes.moe could not be reached")
                    : Resolution.Without(MatchState.NotFound, "no entry on animethemes.moe for its IDs or title");
            }
        }

        return results;
    }

    /// <summary>
    /// Gets an anime by ID, through the cache.
    /// </summary>
    /// <param name="animeId">animethemes.moe anime ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime, or null when it does not exist.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Anime?> GetAnimeAsync(int animeId, CancellationToken cancellationToken)
    {
        var key = "anime:" + animeId.ToString(CultureInfo.InvariantCulture);
        if (_cache.TryGet(key, out var cached))
        {
            return cached?.FirstOrDefault();
        }

        var anime = await _client.GetAnimeAsync(animeId, cancellationToken).ConfigureAwait(false);
        if (anime == null)
        {
            _cache.SetNegative(key);
        }
        else
        {
            _cache.SetPositive(key, [anime]);
        }

        return anime;
    }

    /// <summary>
    /// Finds anime by AniList IDs, through the cache.
    /// </summary>
    /// <param name="aniListIds">AniList IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entries with themes, in the order of the IDs.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<List<Anime>> FindByAniListAsync(IEnumerable<int> aniListIds, CancellationToken cancellationToken)
    {
        var ids = aniListIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
        var found = new Dictionary<string, Anime[]>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var id in ids)
        {
            if (_cache.TryGet(CacheKey(Sites.AniList, id), out var cached))
            {
                found[id] = cached ?? [];
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            var fetched = await _client.FindByExternalIdsAsync(Sites.AniList, missing, cancellationToken).ConfigureAwait(false);
            foreach (var id in missing)
            {
                var usable = fetched.TryGetValue(id, out var animes) ? animes.Where(AnimeThemeAvailability.HasUsableTheme).ToArray() : [];
                if (usable.Length == 0)
                {
                    _cache.SetNegative(CacheKey(Sites.AniList, id));
                }
                else
                {
                    _cache.SetPositive(CacheKey(Sites.AniList, id), usable);
                }

                found[id] = usable;
            }
        }

        return ids.SelectMany(id => found.TryGetValue(id, out var animes) ? animes.Take(1) : []).DistinctBy(anime => anime.Id).ToList();
    }

    /// <summary>
    /// Forgets the cached lookups of an item, so the next check asks again.
    /// </summary>
    /// <param name="item">The item.</param>
    public void Forget(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var providerKey in Sites.DefaultPriority)
        {
            if (Sites.ProviderToSite.TryGetValue(providerKey, out var site) && item.TryGetProviderId(providerKey, out var id) && !string.IsNullOrWhiteSpace(id))
            {
                _cache.Remove(CacheKey(site, id.Trim()));
            }
        }
    }

    /// <summary>
    /// Chooses among several entries linked to the same external ID.
    /// </summary>
    /// <remarks>
    /// 1.x processed every one of them in turn, and each run deleted the files of the previous one as
    /// unexpected, so such items downloaded and deleted themes on every check.
    /// </remarks>
    /// <param name="item">Library item.</param>
    /// <param name="candidates">Entries.</param>
    /// <returns>The best entry.</returns>
    internal static Anime Choose(BaseItem item, IReadOnlyList<Anime> candidates)
    {
        var wantsMovie = item.GetBaseItemKind() == BaseItemKind.Movie;
        return candidates
            .OrderBy(anime => item.ProductionYear is { } year && anime.Year is { } animeYear ? Math.Min(Math.Abs(year - animeYear), 5) : 3)
            .ThenBy(anime => string.Equals(anime.MediaFormat, "Movie", StringComparison.OrdinalIgnoreCase) == wantsMovie ? 0 : 1)
            .ThenByDescending(anime => anime.Themes?.Count ?? 0)
            .ThenBy(anime => anime.Id)
            .First();
    }

    private static string CacheKey(string site, string externalId) => site + ":" + externalId;

    private static string Describe(string providerKey, string externalId)
        => string.Create(CultureInfo.InvariantCulture, $"{providerKey} ID {externalId}");
}
