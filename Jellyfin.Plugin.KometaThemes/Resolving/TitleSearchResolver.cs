using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Finds an item's animethemes.moe entry by its titles, when none of its IDs is known there.
/// </summary>
public sealed partial class TitleSearchResolver
{
    private const int MaxSearches = 4;
    private const int MaxDetailFetches = 3;

    private readonly AnimeThemesClient _client;
    private readonly IResolutionCache _cache;
    private readonly ILogger<TitleSearchResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleSearchResolver"/> class.
    /// </summary>
    /// <param name="client">animethemes.moe client.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="logger">Logger.</param>
    public TitleSearchResolver(AnimeThemesClient client, IResolutionCache cache, ILogger<TitleSearchResolver> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Finds an item's anime by its titles.
    /// </summary>
    /// <param name="item">Library item.</param>
    /// <param name="threshold">Required confidence, 0.5 to 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The match and a description of it, or null when no candidate is confident enough.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<(Anime Anime, string Detail)?> ResolveAsync(BaseItem item, double threshold, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var query = QueryFor(item);
        if (query.Titles.Count == 0)
        {
            return null;
        }

        var required = RequiredScore(threshold);
        var cacheKey = CacheKey(query);
        if (_cache.TryGet(cacheKey, out var cached))
        {
            return cached is { Length: > 0 } ? (cached[0], Describe(query, cached[0])) : null;
        }

        var candidates = new Dictionary<int, Anime>();
        var searched = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<(Anime Anime, int Score)> ranked = [];
        foreach (var text in query.Titles.Concat(query.WeakTitles))
        {
            var folded = string.Concat(TitleMatcher.Words(text));
            if (folded.Length < 2 || !searched.Add(folded) || searched.Count > MaxSearches)
            {
                continue;
            }

            foreach (var anime in await _client.SearchAsync(text, cancellationToken).ConfigureAwait(false))
            {
                candidates.TryAdd(anime.Id, anime);
            }

            ranked = TitleMatcher.Rank(query, candidates.Values);
            if (ranked.Count > 0 && ranked[0].Score >= TitleMatcher.Exact - 5)
            {
                break;
            }
        }

        foreach (var (candidate, score) in ranked.Where(r => r.Score >= required).Take(MaxDetailFetches))
        {
            // Search results carry theme stubs only; the full entry tells whether anything can be downloaded.
            var full = await _client.GetAnimeAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            if (full == null || !AnimeThemeAvailability.HasUsableTheme(full))
            {
                continue;
            }

            _logger.LogInformation("Title match for {Name}: {Match} (score {Score}, required {Required})", item.Name, full.Name, score, required);
            _cache.SetPositive(cacheKey, [full]);
            return (full, Describe(query, full));
        }

        _logger.LogDebug(
            "No title match for {Name}: best {Best} ({Score}, required {Required})",
            item.Name,
            ranked.Count > 0 ? ranked[0].Anime.Name : "nothing",
            ranked.Count > 0 ? ranked[0].Score : 0,
            required);
        _cache.SetNegative(cacheKey);
        return null;
    }

    /// <summary>
    /// Scores search results against the search text, for the manual search: what the owner typed
    /// decides, the item only tells the format and the year.
    /// </summary>
    /// <param name="item">The series, season or movie being matched, or null.</param>
    /// <param name="search">The search text.</param>
    /// <param name="candidates">Search results.</param>
    /// <returns>Candidates with a 0-100 score, best first.</returns>
    public static IReadOnlyList<(Anime Anime, int Score)> Rank(BaseItem? item, string search, IEnumerable<Anime> candidates)
    {
        var years = new List<int>();
        if (item?.PremiereDate is { } premiere)
        {
            years.Add(premiere.Year);
        }
        else if (item?.ProductionYear is { } year)
        {
            years.Add(year);
        }

        return TitleMatcher.Rank(new TitleQuery([search], [], years, null, item is Movie), candidates);
    }

    /// <summary>
    /// Gets the cache key of a title lookup.
    /// </summary>
    /// <param name="query">The item's titles.</param>
    /// <returns>The key.</returns>
    internal static string CacheKey(TitleQuery query)
        => "title:" + string.Join('|', query.Titles.Select(t => string.Concat(TitleMatcher.Words(t))))
            + ":" + string.Join(',', query.Years) + (query.IsMovie ? ":movie" : string.Empty);

    /// <summary>
    /// Converts a 0.5-1 threshold to a score.
    /// </summary>
    /// <param name="threshold">The threshold.</param>
    /// <returns>The required score.</returns>
    internal static int RequiredScore(double threshold)
        => Math.Clamp((int)Math.Round(threshold * 100, MidpointRounding.AwayFromZero), 50, TitleMatcher.Exact);

    /// <summary>
    /// Collects what an item says about its title: names, folder, slugs on other sites, years.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The query.</returns>
    internal static TitleQuery QueryFor(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var titles = new List<string>();
        var years = new List<int>();
        int? titleYear = null;

        void Add(string? raw, bool isName)
        {
            var (title, year) = Clean(raw);
            if (year is { } y)
            {
                years.Add(y);
                if (isName)
                {
                    titleYear = y;
                }
            }

            if (title.Length > 0 && titles.All(t => !string.Equals(string.Concat(TitleMatcher.Words(t)), string.Concat(TitleMatcher.Words(title)), StringComparison.Ordinal)))
            {
                titles.Add(title);
            }
        }

        Add(item.Name, true);
        Add(item.OriginalTitle, false);
        Add(FolderName(item), false);
        Add(Slug(Provider(item, "AnimeClick")), false);
        Add(Slug(Provider(item, "TvdbSlug")), false);

        if (item.ProductionYear is { } production)
        {
            years.Add(production);
        }

        if (item.PremiereDate is { } premiere)
        {
            years.Add(premiere.Year);
        }

        // "Paprika - Sognando un sogno": the original title is the first part.
        var weak = titles
            .SelectMany(t => TitleSeparatorRegex().Split(t))
            .Select(part => part.Trim())
            .Where(part => part.Length >= 3 && titles.All(t => !string.Equals(t, part, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TitleQuery(titles, weak, years.Distinct().ToList(), titleYear, item is Movie);
    }

    private static string? Provider(BaseItem item, string key)
        => item.ProviderIds != null && item.ProviderIds.TryGetValue(key, out var value) ? value : null;

    private static string? FolderName(BaseItem item)
    {
        var path = item.IsFolder ? item.Path : item.ContainingFolderPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (item is Movie { IsInMixedFolder: true })
        {
            name = Path.GetFileNameWithoutExtension(item.Path);
        }

        return name;
    }

    /// <summary>
    /// Takes the title part of a folder or provider value: "Aura (2013) [1080p x265]" gives "Aura" and 2013.
    /// </summary>
    /// <param name="raw">The value.</param>
    /// <returns>The title, and a year written in it.</returns>
    internal static (string Title, int? Year) Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, null);
        }

        var text = raw.Trim();

        // Release names use dots or underscores for spaces; folder names use + for a slash.
        if (!text.Contains(' ', StringComparison.Ordinal))
        {
            text = text.Replace('.', ' ').Replace('_', ' ');
        }

        text = text.Replace('+', '/');
        text = BracketsRegex().Replace(text, " ");

        int? year = null;
        var match = YearRegex().Match(text);
        if (match.Success && match.Index > 0)
        {
            year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
            text = text[..match.Index];
        }

        text = ReleaseTagsRegex().Replace(text, " ");
        text = SpacesRegex().Replace(text, " ").Trim(' ', '-', '.', ',', ';');
        return (text, year);
    }

    private static string? Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // AnimeClick stores "26918/honzuki-no-gekokujou".
        var slug = value.Trim();
        var slash = slug.LastIndexOf('/');
        if (slash >= 0)
        {
            slug = slug[(slash + 1)..];
        }

        slug = slug.Replace('-', ' ').Replace('_', ' ').Trim();
        return slug.Length >= 3 && !slug.All(char.IsDigit) ? slug : null;
    }

    private static string Describe(TitleQuery query, Anime anime)
    {
        var title = query.Titles.Concat(query.WeakTitles)
            .Select(t => (Title: t, Score: TitleMatcher.Score(query with { Titles = [t], WeakTitles = [] }, anime)))
            .OrderByDescending(pair => pair.Score)
            .Select(pair => pair.Title)
            .FirstOrDefault() ?? anime.Name;
        return "title match “" + title + "”";
    }

    [GeneratedRegex(@"\s+[-–:|]\s+|:\s+", RegexOptions.CultureInvariant)]
    private static partial Regex TitleSeparatorRegex();

    [GeneratedRegex(@"\[[^\]]*\]|\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex BracketsRegex();

    [GeneratedRegex(@"\(?\b(?<y>(?:19[5-9]|20[0-3])\d)\b\)?", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\b(?:\d{3,4}p|2160p|4k|8k|uhd|hdr\d*|dv|x26[45]|h\.?26[45]|hevc|av1|avc|10bit|8bit|aac\d?(?:\.\d)?|ac3|eac3|dts(?:-hd)?|flac|opus|truehd|atmos|bd(?:rip|remux)?|bluray|blu-ray|web(?:-?dl|rip)?|dvdrip|remux|dual[ -]audio|multi|ita|eng|jap|jpn|sub(?:s|bed)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagsRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();
}
