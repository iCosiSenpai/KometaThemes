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
/// Client of the animethemes.moe API.
/// </summary>
/// <remarks>
/// Failures are reported in one way only: a request that could not be answered (network error,
/// timeout, 5xx after retries, unreadable body) throws <see cref="HttpRequestException"/>, while an
/// answer that simply has no data returns an empty result. Callers can then tell "not on
/// animethemes.moe" from "animethemes.moe is unreachable", which 1.x mixed up: an outage was cached as
/// "not found" for a day, and some calls threw types no caller handled.
/// </remarks>
public sealed class AnimeThemesClient
{
    /// <summary>Name of the configured HTTP client.</summary>
    public const string HttpClientName = "AnimeThemes";

    /// <summary>Relations included with every anime: covers, songs and artists, entries, videos, audio, external IDs.</summary>
    internal const string DetailInclude = "images,animethemes.song.artists,animethemes.animethemeentries.videos.audio,resources";

    private const int PageSize = 100;
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
    /// Searches anime by title.
    /// </summary>
    /// <param name="title">Search text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching anime with covers and theme stubs, best first as ranked by the API.</returns>
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
            ["include[anime]"] = "images,animethemes",
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
        var uri = QueryHelpers.AddQueryString("anime", new Dictionary<string, string?>
        {
            ["filter[id]"] = id.ToString(CultureInfo.InvariantCulture),
            ["include"] = DetailInclude,
        });

        var response = await GetJsonAsync<AnimeResponse>(uri, cancellationToken).ConfigureAwait(false);
        return response?.Anime?.FirstOrDefault(anime => anime.Id == id);
    }

    /// <summary>
    /// Gets one anime by slug with all its themes.
    /// </summary>
    /// <param name="slug">Anime slug.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime, or null when it does not exist.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Anime?> GetAnimeBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var uri = QueryHelpers.AddQueryString("anime/" + Uri.EscapeDataString(slug.Trim()), "include", DetailInclude);
        using var document = await GetDocumentAsync(uri, cancellationToken).ConfigureAwait(false);
        if (document == null || !document.RootElement.TryGetProperty("anime", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Read<Anime>(element);
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
