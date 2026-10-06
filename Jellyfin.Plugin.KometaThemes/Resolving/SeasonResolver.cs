using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.AniList;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Finds the anime entry of each season after the first.
/// </summary>
public sealed class SeasonResolver
{
    private const int MaxChainLength = 12;

    private readonly AniListClient _aniList;
    private readonly AnimeResolver _animes;
    private readonly IResolutionCache _cache;
    private readonly ILogger<SeasonResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeasonResolver"/> class.
    /// </summary>
    /// <param name="aniList">AniList client.</param>
    /// <param name="animes">Anime resolver, for lookups by AniList ID.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="logger">Logger.</param>
    public SeasonResolver(AniListClient aniList, AnimeResolver animes, IResolutionCache cache, ILogger<SeasonResolver> logger)
    {
        _aniList = aniList;
        _animes = animes;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves one season.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <param name="seriesResolution">How the series itself was resolved.</param>
    /// <param name="seasonId">Library ID of the season.</param>
    /// <param name="season">What the library knows about the season.</param>
    /// <param name="config">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The season's resolution: its own entries, or inherit the series themes.</returns>
    public async Task<Resolution> ResolveAsync(
        BaseItem series,
        Resolution seriesResolution,
        Guid seasonId,
        LibrarySeason season,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(seriesResolution);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            var binding = config.FindBinding(seasonId);
            if (binding != null)
            {
                var chosen = await _animes.GetAnimeAsync(binding.AnimeId, cancellationToken).ConfigureAwait(false);
                if (chosen != null)
                {
                    return Resolution.Found([chosen], "matched by you", manual: true);
                }
            }

            if (!config.PerSeasonThemes)
            {
                return Resolution.Without(MatchState.Inherit, "plays the series themes (per-season themes are off)");
            }

            if (season.Number <= 1)
            {
                return Resolution.Without(MatchState.Inherit, "plays the series themes");
            }

            if (seriesResolution.State != MatchState.Matched)
            {
                return Resolution.Without(MatchState.Inherit, "the series has no match yet");
            }

            var rootId = seriesResolution.Animes[0].GetExternalId(Sites.AniList) ?? SeriesAniListId(series);
            if (rootId == null)
            {
                return Resolution.Without(MatchState.Inherit, "plays the series themes: no AniList ID to follow its sequels");
            }

            var chain = await GetChainAsync(rootId.Value, cancellationToken).ConfigureAwait(false);
            var match = SeasonMatcher.Match(chain, season);
            if (match == null)
            {
                return Resolution.Without(MatchState.Inherit, "plays the series themes: no sequel on AniList matches its year and episodes");
            }

            var animes = await _animes.FindByAniListAsync(match.Entries.Select(entry => entry.Id), cancellationToken).ConfigureAwait(false);
            if (animes.Count == 0)
            {
                return Resolution.Without(MatchState.Inherit, "plays the series themes: its sequel has no themes on animethemes.moe yet");
            }

            return Resolution.Found(animes, match.Reason);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not resolve season {Number} of {Name}", season.Number, series.Name);
            return Resolution.Without(MatchState.Unavailable, "AniList or animethemes.moe could not be reached");
        }
    }

    /// <summary>
    /// Forgets the cached sequel chain of a series.
    /// </summary>
    /// <param name="aniListId">AniList ID of the first season.</param>
    public void Forget(int aniListId) => _cache.Remove(ChainKey(aniListId));

    private async Task<IReadOnlyList<AniListSeason>> GetChainAsync(int rootId, CancellationToken cancellationToken)
    {
        var key = ChainKey(rootId);
        if (_cache.TryGetText(key, out var json) && json != null)
        {
            try
            {
                var cached = JsonSerializer.Deserialize<List<AniListSeason>>(json);
                if (cached is { Count: > 0 })
                {
                    return cached;
                }
            }
            catch (JsonException)
            {
                _cache.Remove(key);
            }
        }

        var chain = await _aniList.GetSequelChainAsync(rootId, MaxChainLength, cancellationToken).ConfigureAwait(false);
        if (chain.Count > 0)
        {
            _cache.SetText(key, JsonSerializer.Serialize(chain));
        }

        return chain;
    }

    private static int? SeriesAniListId(BaseItem series)
        => series.TryGetProviderId("AniList", out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string ChainKey(int rootId) => "anilist-chain:" + rootId.ToString(CultureInfo.InvariantCulture);
}
