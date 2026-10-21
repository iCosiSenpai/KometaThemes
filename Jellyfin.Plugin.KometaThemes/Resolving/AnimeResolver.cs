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
/// in the configured order, then the titles.
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
        string? unavailable = null;

        foreach (var item in items)
        {
            if (config.FindBinding(item.Id) is not { } binding)
            {
                continue;
            }

            try
            {
                if (await GetAnimeAsync(binding.AnimeId, cancellationToken).ConfigureAwait(false) is { } anime)
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
                results[item.Id] = Unreachable();
            }
        }

        foreach (var providerKey in Sites.NormalizeProviderPriority(config.ProviderPriority))
        {
            var pending = items.Where(item => !results.ContainsKey(item.Id)).ToList();
            if (pending.Count == 0 || unavailable != null)
            {
                break;
            }

            var site = Sites.ProviderToSite[providerKey];
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

            if (byId.Count == 0)
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
                break;
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
                    if (await _titles.ResolveAsync(item, config.TitleMatchThreshold, cancellationToken).ConfigureAwait(false) is { } match)
                    {
                        results[item.Id] = Resolution.Found([match.Anime], match.Detail);
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "animethemes.moe search failed for {Name}", item.Name);
                    unavailable = ex.Message;
                }
            }
        }

        foreach (var item in items.Where(item => !results.ContainsKey(item.Id)))
        {
            results[item.Id] = unavailable != null
                ? Unreachable()
                : Resolution.Without(MatchState.NotFound, config.EnableTitleFallback
                    ? "no entry on animethemes.moe for its IDs or titles"
                    : "no entry on animethemes.moe for its IDs");
        }

        return results;
    }

    /// <summary>
    /// Gets an anime with all its themes, through the cache.
    /// </summary>
    /// <param name="animeId">animethemes.moe anime ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime, or null when it does not exist.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Anime?> GetAnimeAsync(int animeId, CancellationToken cancellationToken)
        => (await GetAnimeAsync([animeId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(animeId);

    /// <summary>
    /// Gets several anime with all their themes, through the cache, missing ones in batches.
    /// </summary>
    /// <param name="animeIds">animethemes.moe anime IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime that exist, by ID.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Dictionary<int, Anime>> GetAnimeAsync(IEnumerable<int> animeIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, Anime>();
        var missing = new List<int>();
        foreach (var id in animeIds.Distinct())
        {
            if (_cache.TryGet(AnimeKey(id), out var cached))
            {
                if (cached is { Length: > 0 })
                {
                    result[id] = cached[0];
                }
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            var fetched = (await _client.GetAnimeAsync(missing, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
            foreach (var id in missing)
            {
                if (fetched.TryGetValue(id, out var anime))
                {
                    _cache.SetPositive(AnimeKey(id), [anime]);
                    result[id] = anime;
                }
                else
                {
                    _cache.SetNegative(AnimeKey(id));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Finds anime by AniList IDs, through the cache.
    /// </summary>
    /// <param name="aniListIds">AniList IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>For each AniList ID, the animethemes.moe anime linked to it (the first when several are).</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Dictionary<int, Anime>> FindByAniListAsync(IEnumerable<int> aniListIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, Anime>();
        var missing = new List<int>();
        foreach (var id in aniListIds.Distinct())
        {
            if (_cache.TryGet(CacheKey(Sites.AniList, Text(id)), out var cached))
            {
                if (cached is { Length: > 0 })
                {
                    result[id] = cached[0];
                }
            }
            else
            {
                missing.Add(id);
            }
        }

        if (missing.Count > 0)
        {
            var fetched = await _client.FindByExternalIdsAsync(Sites.AniList, missing.Select(Text), cancellationToken).ConfigureAwait(false);
            foreach (var id in missing)
            {
                var animes = fetched.TryGetValue(Text(id), out var list) ? list : [];
                if (animes.Length == 0)
                {
                    _cache.SetNegative(CacheKey(Sites.AniList, Text(id)));
                    continue;
                }

                _cache.SetPositive(CacheKey(Sites.AniList, Text(id)), animes);
                result[id] = animes.OrderBy(a => a.Id).First();
            }
        }

        return result;
    }

    /// <summary>
    /// Forgets the cached lookups of an item, so the next check asks again.
    /// </summary>
    /// <param name="item">The item.</param>
    public void Forget(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var (providerKey, site) in Sites.ProviderToSite)
        {
            if (item.TryGetProviderId(providerKey, out var id) && !string.IsNullOrWhiteSpace(id))
            {
                _cache.Remove(CacheKey(site, id.Trim()));
            }
        }

        _cache.Remove(TitleSearchResolver.CacheKey(TitleSearchResolver.QueryFor(item)));
    }

    /// <summary>
    /// Forgets the cached details of anime entries, so their themes are read again.
    /// </summary>
    /// <param name="animeIds">animethemes.moe anime IDs.</param>
    public void ForgetAnime(IEnumerable<int> animeIds)
    {
        ArgumentNullException.ThrowIfNull(animeIds);
        foreach (var id in animeIds)
        {
            _cache.Remove(AnimeKey(id));
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

    private static Resolution Unreachable() => Resolution.Without(MatchState.Unavailable, "animethemes.moe could not be reached");

    private static string Text(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string AnimeKey(int id) => "anime:" + Text(id);

    private static string CacheKey(string site, string externalId) => site + ":" + externalId;

    private static string Describe(string providerKey, string externalId)
        => string.Create(CultureInfo.InvariantCulture, $"{providerKey} ID {externalId}");
}
