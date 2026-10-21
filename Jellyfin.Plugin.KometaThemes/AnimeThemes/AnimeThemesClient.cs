using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.AnimeThemes;

/// <summary>
/// Client of the animethemes.moe JSON:API.
/// </summary>
/// <remarks>
/// <para>
/// Failures are reported in one way only: a request that could not be answered (network error,
/// timeout, 5xx after retries, unreadable body) throws <see cref="HttpRequestException"/>, while an
/// answer that simply has no data returns an empty result. Callers can then tell "not on
/// animethemes.moe" from "animethemes.moe is unreachable".
/// </para>
/// <para>
/// Filters are always scoped to the anime type (<c>filter[anime][id]</c>). The API applies an
/// unscoped filter to every included relation too, so 2.0's <c>filter[id]=1386</c> also filtered the
/// themes, resources and images by that ID and returned an anime with none of them: every title match
/// and every manual match then looked like an entry without themes.
/// </para>
/// </remarks>
public sealed class AnimeThemesClient
{
    /// <summary>Name of the configured HTTP client.</summary>
    public const string HttpClientName = "AnimeThemes";

    /// <summary>Relations included with every anime: covers, songs and artists, versions, files, external IDs, franchise, other titles.</summary>
    internal const string DetailInclude = "images,animethemes.song.artists,animethemes.animethemeentries.videos.audio,resources,series,animesynonyms";

    /// <summary>Largest page the API serves.</summary>
    private const int PageSize = 100;

    /// <summary>Anime per detail request: each carries all its themes and files, so pages stay small.</summary>
    private const int DetailBatch = 10;

    private const int MaxPages = 5;

    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<AnimeThemesClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeThemesClient"/> class.
    /// </summary>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public AnimeThemesClient(IHttpClientFactory clientFactory, ILogger<AnimeThemesClient> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Finds anime by their ID on another site, in batches.
    /// </summary>
    /// <remarks>
    /// The resource filter also limits the included resources to that site, so the anime returned
    /// here carry only the link that matched. <see cref="GetAnimeAsync(IEnumerable{int}, CancellationToken)"/> returns them all.
    /// </remarks>
    /// <param name="site">Site name as animethemes.moe spells it, such as <c>AniList</c>.</param>
    /// <param name="ids">IDs on that site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An entry for every requested ID; empty when animethemes.moe has none.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Dictionary<string, Anime[]>> FindByExternalIdsAsync(string site, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var requested = ids
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new Dictionary<string, Anime[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in requested.Chunk(PageSize))
        {
            // Only numeric IDs can match: the API stores external IDs as integers. Anything else is
            // answered locally instead of producing a malformed filter.
            var numeric = batch
                .Select(id => (Raw: id, Ok: int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n), Value: n))
                .ToArray();

            var found = new Dictionary<int, List<Anime>>();
            var valid = numeric.Where(n => n.Ok).Select(n => n.Value).Distinct().ToArray();
            if (valid.Length > 0)
            {
                var query = new Dictionary<string, string?>
                {
                    ["filter[resource][external_id]"] = string.Join(',', valid.Select(v => v.ToString(CultureInfo.InvariantCulture))),
                    ["filter[resource][site]"] = site,

                    // Without this the resource filter is ignored and the API returns an arbitrary page.
                    ["filter[has]"] = "resources",
                    ["page[size]"] = PageSize.ToString(CultureInfo.InvariantCulture),
                    ["include"] = DetailInclude,
                };

                foreach (var anime in await GetAllPagesAsync(QueryHelpers.AddQueryString("anime", query), cancellationToken).ConfigureAwait(false))
                {
                    // Re-check the link: never trust that a returned anime belongs to a requested ID.
                    foreach (var resource in anime.Resources ?? [])
                    {
                        if (resource.ExternalId is { } externalId
                            && string.Equals(resource.Site, site, StringComparison.OrdinalIgnoreCase)
                            && valid.Contains(externalId))
                        {
                            if (!found.TryGetValue(externalId, out var list))
                            {
                                found[externalId] = list = [];
                            }

                            if (list.All(a => a.Id != anime.Id))
                            {
                                list.Add(anime);
                            }
                        }
                    }
                }
            }

            foreach (var (raw, ok, value) in numeric)
            {
                result[raw] = ok && found.TryGetValue(value, out var list) ? list.ToArray() : [];
            }
        }

        _logger.LogDebug(
            "animethemes.moe lookup by {Site}: {Found} of {Requested} IDs found",
            site,
            result.Count(pair => pair.Value.Length > 0),
            requested.Length);
        return result;
    }

    /// <summary>
    /// Searches anime by title, synonyms included.
    /// </summary>
    /// <param name="title">Search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching anime with covers, other titles, franchise and theme stubs, best first as ranked by the API.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Anime[]> SearchAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var uri = QueryHelpers.AddQueryString("search", new Dictionary<string, string?>
        {
            ["q"] = title.Trim(),
            ["fields[search]"] = "anime",
            ["include[anime]"] = "images,animethemes,animesynonyms,series",
            ["page[limit]"] = "15",
        });

        var response = await GetJsonAsync<SearchResponse>(uri, cancellationToken).ConfigureAwait(false);
        return response?.Search?.Anime?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets one anime with all its themes.
    /// </summary>
    /// <param name="id">animethemes.moe anime ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime, or null when it does not exist.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Anime?> GetAnimeAsync(int id, CancellationToken cancellationToken)
    {
        var found = await GetAnimeAsync([id], cancellationToken).ConfigureAwait(false);
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>
    /// Gets several anime with all their themes.
    /// </summary>
    /// <param name="ids">animethemes.moe anime IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime that exist, in no particular order.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<IReadOnlyList<Anime>> GetAnimeAsync(IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Where(id => id > 0).Distinct().ToArray();
        var result = new List<Anime>();
        foreach (var batch in wanted.Chunk(DetailBatch))
        {
            var uri = QueryHelpers.AddQueryString("anime", new Dictionary<string, string?>
            {
                ["filter[anime][id]"] = string.Join(',', batch.Select(id => id.ToString(CultureInfo.InvariantCulture))),
                ["page[size]"] = PageSize.ToString(CultureInfo.InvariantCulture),
                ["include"] = DetailInclude,
            });

            // Re-check the IDs: an ignored filter must never hand back unrelated anime.
            result.AddRange((await GetAllPagesAsync(uri, cancellationToken).ConfigureAwait(false)).Where(anime => batch.Contains(anime.Id)));
        }

        return result.DistinctBy(anime => anime.Id).ToList();
    }

    /// <summary>
    /// Gets the anime animethemes.moe groups into one franchise.
    /// </summary>
    /// <param name="slug">Series slug, such as <c>kaguya_sama_wa_kokurasetai</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries of the franchise without their themes; empty when the series does not exist.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<IReadOnlyList<Anime>> GetSeriesAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return [];
        }

        var uri = QueryHelpers.AddQueryString("series/" + Uri.EscapeDataString(slug.Trim()), "include", "anime");
        using var document = await GetDocumentAsync(uri, cancellationToken).ConfigureAwait(false);
        if (document == null
            || !document.RootElement.TryGetProperty("series", out var series)
            || series.ValueKind != JsonValueKind.Object
            || !series.TryGetProperty("anime", out var members)
            || members.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return Read<List<Anime>>(members) ?? [];
    }

    private async Task<List<Anime>> GetAllPagesAsync(string firstUri, CancellationToken cancellationToken)
    {
        var all = new List<Anime>();
        string? next = firstUri;
        for (var page = 0; page < MaxPages && next != null; page++)
        {
            using var document = await GetDocumentAsync(next, cancellationToken).ConfigureAwait(false);
            if (document == null)
            {
                break;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("anime", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                all.AddRange(Read<List<Anime>>(list) ?? []);
            }

            next = root.TryGetProperty("links", out var links)
                && links.TryGetProperty("next", out var nextElement)
                && nextElement.ValueKind == JsonValueKind.String
                    ? nextElement.GetString()
                    : null;
        }

        return all;
    }

    private async Task<T?> GetJsonAsync<T>(string uri, CancellationToken cancellationToken)
        where T : class
    {
        using var document = await GetDocumentAsync(uri, cancellationToken).ConfigureAwait(false);
        return document == null ? null : Read<T>(document.RootElement);
    }

    private static T? Read<T>(JsonElement element)
        where T : class
    {
        try
        {
            return element.Deserialize<T>();
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("animethemes.moe sent data in an unexpected shape.", ex);
        }
    }

    /// <summary>
    /// Fetches a JSON document. Null means "no such resource"; anything that prevents an answer throws.
    /// </summary>
    private async Task<JsonDocument?> GetDocumentAsync(string uri, CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            // 4xx other than 404 means the request itself is wrong; still not "no data", so report it.
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"animethemes.moe answered {(int)response.StatusCode} for {uri}"),
                null,
                response.StatusCode);
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("animethemes.moe sent a response that is not valid JSON.", ex);
        }
    }
}
