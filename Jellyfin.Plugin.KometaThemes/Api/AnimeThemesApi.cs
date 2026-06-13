using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Represents the AnimeThemes API with support for multi-site external ID lookup and title search.
/// </summary>
public sealed class AnimeThemesApi : IDisposable
{
    private const string AnimeDetailInclude = "images,animethemes.animethemeentries.videos.audio,resources";

    private readonly HttpClient _client;
    private readonly ILogger<AnimeThemesApi> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeThemesApi"/> class.
    /// </summary>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger instance.</param>
    public AnimeThemesApi(IHttpClientFactory clientFactory, ILogger<AnimeThemesApi> logger)
    {
        _logger = logger;
        _client = clientFactory.CreateClient("AnimeThemes");
    }

    /// <summary>
    /// Finds anime with all their themes by external IDs for a given site.
    /// </summary>
    /// <param name="site">The site name (e.g. "AniDB", "MyAnimeList").</param>
    /// <param name="ids">External IDs on the given site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary with an entry for each passed ID.</returns>
    public async ValueTask<Dictionary<string, Anime[]>> FindByExternalIdsAsync(
        string site,
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToArray();

        _logger.LogInformation(
            "Looking up {Count} shows on {Site} in the AnimeThemes db...",
            idList.Length,
            site);

        if (idList.Length > 100)
        {
            _logger.LogWarning("{Site} batch has {Count} IDs — paginating to 100 max per request", site, idList.Length);
            idList = idList.Take(100).ToArray();
        }

        var arguments = new Dictionary<string, string?>
        {
            { "filter[resource][external_id]", string.Join(",", idList) },
            { "filter[resource][site]", site },
            { "filter[has]", "resources" },
            { "page[size]", idList.Length.ToString(CultureInfo.InvariantCulture) },
            { "include", AnimeDetailInclude },
        };

        var uri = QueryHelpers.AddQueryString("/anime/", arguments);

        _logger.LogInformation("Fetching from API: {Uri} (length={Length})", uri, uri.Length);
        var result = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccessStatusCode();

        var content = await result.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var contentDisposal = content.ConfigureAwait(false);

        var response = await JsonSerializer.DeserializeAsync<AnimeResponse>(content, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Group results by external ID for the requested site
        var animeByExternalId = new Dictionary<string, List<Anime>>(StringComparer.OrdinalIgnoreCase);
        if (response?.Anime != null)
        {
            foreach (var anime in response.Anime)
            {
                if (anime.Resources == null)
                {
                    continue;
                }

                foreach (var resource in anime.Resources)
                {
                    if (resource.Site == site && resource.ExternalId.HasValue)
                    {
                        var key = resource.ExternalId.Value.ToString(CultureInfo.InvariantCulture);
                        if (!animeByExternalId.TryGetValue(key, out var list))
                        {
                            list = [];
                            animeByExternalId[key] = list;
                        }

                        list.Add(anime);
                    }
                }
            }
        }

        _logger.LogInformation(
            "Of a total of {TotalCount} shows, we were able to find entries for {FinalCount} shows on {Site}",
            idList.Length,
            animeByExternalId.Count,
            site);

        return idList.ToDictionary(
            id => id,
            id => animeByExternalId.TryGetValue(id, out var list) ? list.ToArray() : [],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Searches for anime by title using the search endpoint.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="year">Optional year filter for disambiguation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of matching anime.</returns>
    public async ValueTask<Anime[]> SearchByTitleAsync(
        string title,
        int? year,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Searching AnimeThemes by title: {Title} (year={Year})", title, year);

        var arguments = new Dictionary<string, string?>
        {
            { "q", title },
            { "fields[search]", "anime" },
        };

        var uri = QueryHelpers.AddQueryString("/search/", arguments);

        var result = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogWarning("Search request returned {Status} for title: {Title} (query may be too complex or malformed)", (int)result.StatusCode, title);
            return [];
        }

        var content = await result.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var contentDisposal = content.ConfigureAwait(false);

        var response = await JsonSerializer.DeserializeAsync<SearchResponse>(content, cancellationToken: cancellationToken).ConfigureAwait(false);

        var anime = response?.Search?.Anime?.ToArray() ?? [];

        _logger.LogInformation("Search returned {Count} results for title: {Title}", anime.Length, title);

        return anime;
    }

    /// <summary>
    /// Fetches a single anime with all themes by its AnimeThemes ID.
    /// </summary>
    /// <param name="id">AnimeThemes anime ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime object, or null if not found.</returns>
    public async ValueTask<Anime?> GetAnimeByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching AnimeThemes anime by ID: {Id}", id);

        var arguments = new Dictionary<string, string?>
        {
            { "filter[id]", id.ToString(CultureInfo.InvariantCulture) },
            { "include", AnimeDetailInclude },
        };

        var uri = QueryHelpers.AddQueryString("/anime/", arguments);

        var result = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogWarning("GetAnimeById returned {Status} for ID: {Id}", (int)result.StatusCode, id);
            return null;
        }

        var content = await result.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var contentDisposal = content.ConfigureAwait(false);

        using var doc = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("anime", out var animeElement) || animeElement.GetArrayLength() == 0)
        {
            return null;
        }

        var anime = JsonSerializer.Deserialize<Anime>(animeElement[0].GetRawText());
        if ((anime?.Themes == null || anime.Themes.Count == 0) && !string.IsNullOrWhiteSpace(anime?.Slug))
        {
            _logger.LogInformation(
                "AnimeThemes ID lookup for {Id} returned no themes; retrying show endpoint with slug '{Slug}'",
                id,
                anime.Slug);
            return await GetAnimeBySlugAsync(anime.Slug, cancellationToken).ConfigureAwait(false);
        }

        return anime;
    }

    /// <summary>
    /// Fetches a single anime with all themes by its AnimeThemes slug.
    /// </summary>
    /// <param name="slug">AnimeThemes anime slug.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The anime object, or null if not found.</returns>
    public async ValueTask<Anime?> GetAnimeBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        _logger.LogInformation("Fetching AnimeThemes anime by slug: {Slug}", slug);

        var arguments = new Dictionary<string, string?>
        {
            { "include", AnimeDetailInclude },
        };

        var uri = QueryHelpers.AddQueryString("/anime/" + Uri.EscapeDataString(slug.Trim()), arguments);

        var result = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccessStatusCode)
        {
            _logger.LogWarning("GetAnimeBySlug returned {Status} for slug: {Slug}", (int)result.StatusCode, slug);
            return null;
        }

        var content = await result.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var contentDisposal = content.ConfigureAwait(false);

        using var doc = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("anime", out var animeElement) || animeElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<Anime>(animeElement.GetRawText());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
    }
}
