using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.AniList;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Api;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// A season of the library for season matching.
/// </summary>
/// <param name="SeasonId">Library ID of the season.</param>
/// <param name="Info">Number, premiere and episodes.</param>
/// <param name="HasFolder">Whether it has a folder of its own; a season whose episodes sit in the series folder shares it.</param>
public sealed record SeasonInfo(Guid SeasonId, LibrarySeason Info, bool HasFolder);

/// <summary>
/// The themes of a series and of each of its seasons.
/// </summary>
/// <param name="Root">What the series folder holds.</param>
/// <param name="Seasons">What each season holds, by season ID.</param>
/// <param name="Franchise">The franchise the seasons were matched against, for the page.</param>
public sealed record SeasonPlan(Resolution Root, IReadOnlyDictionary<Guid, Resolution> Seasons, IReadOnlyList<FranchiseEntry> Franchise);

/// <summary>
/// Finds the anime entry of each season of a series, from the series' franchise on animethemes.moe,
/// with AniList's sequel list when AniList is available.
/// </summary>
public sealed class SeasonResolver
{
    private const int MaxSequels = 16;

    private static readonly JsonSerializerOptions CacheJson = new();

    private readonly AnimeThemesClient _client;
    private readonly AniListClient _aniList;
    private readonly AnimeResolver _animes;
    private readonly IResolutionCache _cache;
    private readonly ILogger<SeasonResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeasonResolver"/> class.
    /// </summary>
    /// <param name="client">animethemes.moe client.</param>
    /// <param name="aniList">AniList client.</param>
    /// <param name="animes">Anime resolver, for entries through the cache.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="logger">Logger.</param>
    public SeasonResolver(AnimeThemesClient client, AniListClient aniList, AnimeResolver animes, IResolutionCache cache, ILogger<SeasonResolver> logger)
    {
        _client = client;
        _aniList = aniList;
        _animes = animes;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves every season of a series.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <param name="seriesResolution">How the series itself was resolved.</param>
    /// <param name="seasons">Its seasons, numbered 1 or higher.</param>
    /// <param name="config">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plan: the series folder and every season.</returns>
    public async Task<SeasonPlan> ResolveAsync(
        BaseItem series,
        Resolution seriesResolution,
        IReadOnlyList<SeasonInfo> seasons,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(seriesResolution);
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(config);

        var result = new Dictionary<Guid, Resolution>();
        var automatic = new List<SeasonInfo>();
        foreach (var season in seasons.Where(s => s.SeasonId != Guid.Empty))
        {
            if (config.FindBinding(season.SeasonId) is { } binding)
            {
                try
                {
                    if (await _animes.GetAnimeAsync(binding.AnimeId, cancellationToken).ConfigureAwait(false) is { } chosen)
                    {
                        result[season.SeasonId] = Resolution.Found([chosen], "matched by you", manual: true);
                        continue;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Could not read the entry chosen for season {Number} of {Name}", season.Info.Number, series.Name);
                    result[season.SeasonId] = Resolution.Without(MatchState.Unavailable, "animethemes.moe could not be reached");
                    continue;
                }
            }

            automatic.Add(season);
        }

        var inherit = (string?)null;
        if (!config.PerSeasonThemes)
        {
            inherit = "plays the series themes (per-season themes are off)";
        }
        else if (seriesResolution.State != MatchState.Matched)
        {
            inherit = "plays the series themes: the series has no match yet";
        }

        if (inherit != null || automatic.Count == 0)
        {
            foreach (var season in automatic)
            {
                result[season.SeasonId] = Resolution.Without(MatchState.Inherit, inherit ?? "plays the series themes");
            }

            return new SeasonPlan(seriesResolution, result, []);
        }

        var root = seriesResolution.Animes[0];
        var library = automatic.Where(s => s.Info.EpisodeCount > 0 || s.Info.Year != null).ToList();

        // Most series have one season, the first: it is the series' own entry, nothing to look up.
        if (library.All(s => s.Info.Number == 1))
        {
            foreach (var season in automatic)
            {
                result[season.SeasonId] = Resolution.Without(MatchState.Inherit, "plays the series themes, which are its own");
            }

            return new SeasonPlan(seriesResolution, result, []);
        }

        Franchise franchise;
        IReadOnlyList<SeasonMatch> matches;
        try
        {
            franchise = await LoadFranchiseAsync(root, series, library, withAniList: false, cancellationToken).ConfigureAwait(false);
            matches = SeasonMatcher.Align(library.Select(s => s.Info).ToList(), franchise.Entries, root.Id);

            // AniList confirms sequels and knows exact episode counts: asked only when animethemes.moe
            // alone leaves a season open, because its budget is small and it is often switched off.
            if (franchise.Entries.Count > 1 && matches.Any(m => !m.Matched))
            {
                var enriched = await LoadFranchiseAsync(root, series, library, withAniList: true, cancellationToken).ConfigureAwait(false);
                if (enriched.UsedAniList)
                {
                    franchise = enriched;
                    matches = SeasonMatcher.Align(library.Select(s => s.Info).ToList(), franchise.Entries, root.Id);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not match the seasons of {Name}", series.Name);
            foreach (var season in automatic)
            {
                result[season.SeasonId] = Resolution.Without(MatchState.Unavailable, "animethemes.moe could not be reached");
            }

            return new SeasonPlan(seriesResolution, result, []);
        }

        var byNumber = matches.ToDictionary(m => m.SeasonNumber);
        var rootResolution = seriesResolution;

        // When later seasons continue the series' own entry, the series folder holds only the first
        // season's episodes of it.
        var first = library.FirstOrDefault();
        if (first != null
            && byNumber.TryGetValue(first.Info.Number, out var firstMatch)
            && firstMatch.EntryIds.SequenceEqual([root.Id])
            && matches.Any(m => m.Window != null && m.EntryIds.SequenceEqual([root.Id])))
        {
            rootResolution = seriesResolution with { Window = new EpisodeWindow(1, first.Info.EpisodeCount) };
        }

        foreach (var season in automatic)
        {
            result[season.SeasonId] = Translate(season, byNumber.GetValueOrDefault(season.Info.Number), root, franchise);
        }

        return new SeasonPlan(rootResolution, result, franchise.Entries);
    }

    /// <summary>
    /// Forgets the cached franchise data of a series, so the next check reads it again.
    /// </summary>
    /// <param name="root">The series' entry.</param>
    /// <param name="aniListId">The series' AniList ID, if any.</param>
    public void Forget(Anime root, int? aniListId)
    {
        ArgumentNullException.ThrowIfNull(root);
        foreach (var series in root.Series ?? [])
        {
            if (!string.IsNullOrWhiteSpace(series.Slug))
            {
                _cache.Remove(SeriesKey(series.Slug));
            }
        }

        if (aniListId is { } id)
        {
            _cache.Remove(SequelsKey(id));
        }
    }

    /// <summary>
    /// Lists the franchise of a series for the match dialog: every entry animethemes.moe groups with it.
    /// </summary>
    /// <param name="root">The series' entry.</param>
    /// <param name="series">The series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Entries without their themes, in premiere order.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<IReadOnlyList<Anime>> GetFranchiseAsync(Anime root, BaseItem series, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        var members = await MembersAsync(root, cancellationToken).ConfigureAwait(false);

        // The franchise lists names and dates only; covers and themes come with each entry's details.
        var details = await _animes.GetAnimeAsync(members.Keys, cancellationToken).ConfigureAwait(false);
        return members.Values
            .Select(member => details.GetValueOrDefault(member.Id) ?? member)
            .OrderBy(a => a.Year ?? int.MaxValue)
            .ThenBy(a => QuarterMonth(a.Season) ?? 13)
            .ThenBy(a => a.Id)
            .ToList();
    }

    private static Resolution Translate(SeasonInfo season, SeasonMatch? match, Anime root, Franchise franchise)
    {
        if (match is not { Matched: true })
        {
            return Resolution.Without(MatchState.Inherit, match?.Reason ?? "plays the series themes");
        }

        if (match.EntryIds.SequenceEqual([root.Id]) && match.Window == null)
        {
            return Resolution.Without(MatchState.Inherit, "plays the series themes, which are its own: " + match.Reason);
        }

        var animes = match.EntryIds.Select(id => franchise.Details.GetValueOrDefault(id)).ToList();
        if (animes.Any(a => a == null))
        {
            return Resolution.Without(MatchState.Inherit, "plays the series themes: its entry is no longer on animethemes.moe");
        }

        if (!animes.Any(a => AnimeThemeAvailability.HasUsableTheme(a!)))
        {
            return Resolution.Without(MatchState.Inherit, "plays the series themes: its entry has no themes on animethemes.moe yet");
        }

        if (match.Window is { } window && !ThemeCatalog.All(animes!, window).Any())
        {
            return Resolution.Without(MatchState.Inherit, "plays the series themes: no theme of its entry is listed for its episodes");
        }

        return Resolution.Found(animes!, match.Reason) with { Window = match.Window };
    }

    private async Task<Franchise> LoadFranchiseAsync(Anime root, BaseItem series, IReadOnlyList<SeasonInfo> seasons, bool withAniList, CancellationToken cancellationToken)
    {
        var members = await MembersAsync(root, cancellationToken).ConfigureAwait(false);

        var aniListSeasons = new Dictionary<int, AniListSeason>();
        var usedAniList = false;
        if (withAniList)
        {
            var full = await _animes.GetAnimeAsync(root.Id, cancellationToken).ConfigureAwait(false) ?? root;
            var aniListId = full.GetExternalId(Sites.AniList) ?? SeriesAniListId(series);
            if (aniListId is { } id && await SequelsAsync(id, cancellationToken).ConfigureAwait(false) is { Count: > 0 } chain)
            {
                usedAniList = true;
                var linked = await _animes.FindByAniListAsync(chain.Select(c => c.Id), cancellationToken).ConfigureAwait(false);
                foreach (var link in chain)
                {
                    if (linked.TryGetValue(link.Id, out var anime))
                    {
                        members.TryAdd(anime.Id, anime);
                        aniListSeasons[anime.Id] = link;
                    }
                }
            }
        }

        // Themes are read only for entries a season could be: the right formats and, when the library
        // dates its seasons, the right years. A franchise like Lupin III has dozens of movies and specials.
        var years = seasons.Select(s => s.Info.Year).ToList();
        var dated = years.All(y => y != null) && years.Count > 0;
        var wanted = members.Values
            .Where(a => a.Id == root.Id
                || (a.MediaFormat is not "Movie"
                    && (!dated || (a.Year is { } y && y >= years.Min()!.Value - 1 && y <= years.Max()!.Value + 1))))
            .Select(a => a.Id)
            .ToList();
        var details = await _animes.GetAnimeAsync(wanted, cancellationToken).ConfigureAwait(false);

        var entries = members.Values
            .Select(member =>
            {
                var detail = details.GetValueOrDefault(member.Id);
                aniListSeasons.TryGetValue(member.Id, out var link);
                var episodes = link?.Episodes is > 0 ? link.Episodes : detail == null ? null : ThemeCatalog.LastListedEpisode(detail);
                return new FranchiseEntry(
                    member.Id,
                    member.Name,
                    member.Year ?? link?.Year,
                    link?.Month ?? QuarterMonth(member.Season),
                    member.MediaFormat,
                    episodes,
                    link?.Episodes is > 0,
                    link != null);
            })
            .ToList();

        return new Franchise(entries, details, usedAniList);
    }

    private async Task<Dictionary<int, Anime>> MembersAsync(Anime root, CancellationToken cancellationToken)
    {
        // Entries found by an external ID or a search carry their franchise; a cached 2.0 entry may not.
        var slugs = (root.Series ?? []).Select(s => s.Slug).OfType<string>().ToList();
        if (root.Series == null && await _animes.GetAnimeAsync(root.Id, cancellationToken).ConfigureAwait(false) is { } full)
        {
            slugs = (full.Series ?? []).Select(s => s.Slug).OfType<string>().ToList();
        }

        var members = new Dictionary<int, Anime> { [root.Id] = root };
        foreach (var slug in slugs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var member in await SeriesAsync(slug, cancellationToken).ConfigureAwait(false))
            {
                members.TryAdd(member.Id, member);
            }
        }

        return members;
    }

    private async Task<IReadOnlyList<Anime>> SeriesAsync(string slug, CancellationToken cancellationToken)
    {
        var key = SeriesKey(slug);
        if (_cache.TryGetText(key, out var json) && json != null)
        {
            try
            {
                if (JsonSerializer.Deserialize<List<Anime>>(json, CacheJson) is { } cached)
                {
                    return cached;
                }
            }
            catch (JsonException)
            {
                _cache.Remove(key);
            }
        }

        var members = await _client.GetSeriesAsync(slug, cancellationToken).ConfigureAwait(false);
        _cache.SetText(key, JsonSerializer.Serialize(members, CacheJson));
        return members;
    }

    private async Task<IReadOnlyList<AniListSeason>> SequelsAsync(int aniListId, CancellationToken cancellationToken)
    {
        var key = SequelsKey(aniListId);
        if (_cache.TryGetText(key, out var json) && json != null)
        {
            try
            {
                if (JsonSerializer.Deserialize<List<AniListSeason>>(json, CacheJson) is { Count: > 0 } cached)
                {
                    return cached;
                }
            }
            catch (JsonException)
            {
                _cache.Remove(key);
            }
        }

        try
        {
            var chain = await _aniList.GetSequelChainAsync(aniListId, MaxSequels, cancellationToken).ConfigureAwait(false);
            if (chain.Count > 0)
            {
                _cache.SetText(key, JsonSerializer.Serialize(chain, CacheJson));
            }

            return chain;
        }
        catch (HttpRequestException ex)
        {
            // AniList is an extra: without it the seasons are matched from animethemes.moe alone.
            _logger.LogDebug(ex, "AniList sequels of {Id} not available", aniListId);
            return [];
        }
    }

    private static int? QuarterMonth(string? season) => season?.ToUpperInvariant() switch
    {
        "WINTER" => 1,
        "SPRING" => 4,
        "SUMMER" => 7,
        "FALL" => 10,
        _ => null,
    };

    private static int? SeriesAniListId(BaseItem series)
        => series.TryGetProviderId("AniList", out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string SeriesKey(string slug) => "series:" + slug.Trim().ToLowerInvariant();

    private static string SequelsKey(int aniListId) => "anilist-sequels:" + aniListId.ToString(CultureInfo.InvariantCulture);

    private sealed record Franchise(IReadOnlyList<FranchiseEntry> Entries, IReadOnlyDictionary<int, Anime> Details, bool UsedAniList);
}
