using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.AnimeThemes;

/// <summary>
/// Client of the animethemes.moe GraphQL API.
/// </summary>
/// <remarks>
/// <para>
/// The REST JSON:API this class used before (<c>https://api.animethemes.moe/</c>) answers 503 on every
/// route since November 2026, which left the plugin unable to download anything. The same data is
/// served as GraphQL at <c>https://graphql.animethemes.moe/</c>: one POST per request, carrying the
/// document and its variables. The public surface and the models it returns are unchanged, so
/// <see cref="Resolving.AnimeResolver"/>, <see cref="Resolving.SeasonResolver"/>,
/// <see cref="Resolving.TitleSearchResolver"/> and <see cref="Api.KometaItemsController"/> need no
/// change.
/// </para>
/// <para>
/// Filters are always part of the query itself. The REST API applied an unscoped filter to every
/// included relation too, so 2.0's <c>filter[id]=1386</c> also filtered themes, resources and images
/// by that ID; the GraphQL API has no such trap, and the IDs that come back are re-checked anyway.
/// </para>
/// <para>
/// Failures are reported in one way only: a request that could not be answered (network error,
/// timeout, non-success status after retries, GraphQL <c>errors</c> without data, unreadable body)
/// throws <see cref="HttpRequestException"/>, while an answer with no data returns an empty result.
/// Callers can then tell "not on animethemes.moe" from "animethemes.moe is unreachable".
/// </para>
/// </remarks>
public sealed class AnimeThemesClient
{
    /// <summary>Name of the configured HTTP client.</summary>
    public const string HttpClientName = "AnimeThemes";

    /// <summary>IDs per lookup: the API answers a hundred in one request.</summary>
    private const int LookupBatch = 100;

    /// <summary>Anime per detail request: each carries all its themes and files, so batches stay small.</summary>
    private const int DetailBatch = 10;

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
    /// The answer carries every anime that has a link to that site with one of the requested IDs,
    /// with all its resources; each returned anime is re-checked against the requested link.
    /// </remarks>
    /// <param name="site">Site name as animethemes.moe spells it, such as <c>AniList</c>.</param>
    /// <param name="ids">IDs on that site.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An entry for every requested ID; empty when animethemes.moe has none.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not be reached.</exception>
    public async Task<Dictionary<string, Anime[]>> FindByExternalIdsAsync(string site, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var requested = ids
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new Dictionary<string, Anime[]>(StringComparer.OrdinalIgnoreCase);

        // animethemes.moe only stores IDs of the sites its enum names: anything else cannot match, and
        // asking would be a GraphQL validation error rather than an empty answer.
        var graphQlSite = AnimeThemesMapper.SiteEnum(site);
        if (graphQlSite == null)
        {
            foreach (var id in requested)
            {
                result[id] = [];
            }

            _logger.LogDebug("animethemes.moe stores no {Site} IDs, so none of the {Requested} IDs can match", site, requested.Length);
            return result;
        }

        foreach (var batch in requested.Chunk(LookupBatch))
        {
            // Only numeric IDs can match: the API stores external IDs as integers. Anything else is
            // answered locally instead of producing a malformed variable.
            var numeric = batch
                .Select(id => (Raw: id, Ok: int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n), Value: n))
                .ToArray();

            var found = new Dictionary<int, List<Anime>>();
            var valid = numeric.Where(n => n.Ok).Select(n => n.Value).Distinct().ToArray();
            if (valid.Length > 0)
            {
                var data = await PostAsync(
                    AnimeThemesQueries.FindByExternalIds,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["site"] = graphQlSite,
                        ["ids"] = valid,
                    },
                    cancellationToken).ConfigureAwait(false);

                foreach (var anime in AnimeThemesMapper.ReadAnimeList(AnimeThemesMapper.Field(data, "findAnimeByExternalSite")))
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

        var data = await PostAsync(
            AnimeThemesQueries.Search,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["search"] = title.Trim() },
            cancellationToken).ConfigureAwait(false);
        var search = AnimeThemesMapper.Field(data, "search");
        return AnimeThemesMapper.ReadAnimeList(AnimeThemesMapper.Field(search, "anime")).ToArray();
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
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = ids.Where(id => id > 0).Distinct().ToArray();
        var result = new List<Anime>();
        foreach (var batch in wanted.Chunk(DetailBatch))
        {
            var data = await PostAsync(
                AnimeThemesQueries.AnimeByIds(batch),
                AnimeThemesQueries.AnimeByIdVariables(batch),
                cancellationToken).ConfigureAwait(false);

            // Re-check the IDs: an ignored filter must never hand back unrelated anime.
            foreach (var alias in data.EnumerateObject())
            {
                foreach (var anime in AnimeThemesMapper.ReadAnimeList(AnimeThemesMapper.Field(alias.Value, "data")))
                {
                    if (batch.Contains(anime.Id))
                    {
                        result.Add(anime);
                    }
                }
            }
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

        var data = await PostAsync(
            AnimeThemesQueries.Series,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["slug"] = slug.Trim() },
            cancellationToken).ConfigureAwait(false);
        var series = AnimeThemesMapper.Field(data, "series");
        if (series is not { ValueKind: JsonValueKind.Object })
        {
            return [];
        }

        return AnimeThemesMapper.ReadAnimeList(AnimeThemesMapper.Field(series, "anime")).ToList();
    }

    /// <summary>
    /// Sends one GraphQL document and returns its <c>data</c> object.
    /// </summary>
    /// <remarks>
    /// Everything that prevents an answer throws: the caller must never read an outage as "no data".
    /// </remarks>
    /// <param name="query">GraphQL document.</param>
    /// <param name="variables">Variables of the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <c>data</c> object of the answer.</returns>
    /// <exception cref="HttpRequestException">animethemes.moe could not answer.</exception>
    private async Task<JsonElement> PostAsync(string query, Dictionary<string, object?> variables, CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient(HttpClientName);
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["query"] = query,
            ["variables"] = variables,
        });

        // A string body is buffered, so the retry handler can send the same request again.
        using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Not even a GraphQL answer: the endpoint itself is gone or refusing the request.
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"animethemes.moe answered {(int)response.StatusCode}"),
                null,
                response.StatusCode);
        }

        JsonDocument document;
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("animethemes.moe sent a response that is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var errors = AnimeThemesMapper.Field(root, "errors");
            var data = AnimeThemesMapper.Field(root, "data");
            if (data is { ValueKind: JsonValueKind.Object } value)
            {
                if (errors is { ValueKind: JsonValueKind.Array } list && list.GetArrayLength() > 0)
                {
                    // Partial answers exist (one alias of a batch failed); the data that came back is
                    // still usable, but the failure must be visible in the log.
                    _logger.LogWarning("animethemes.moe answered with GraphQL errors: {Errors}", FirstMessage(list));
                }

                return value.Clone();
            }

            if (errors is { ValueKind: JsonValueKind.Array } failed && failed.GetArrayLength() > 0)
            {
                throw new HttpRequestException("animethemes.moe refused the request: " + FirstMessage(failed));
            }

            throw new HttpRequestException("animethemes.moe sent data in an unexpected shape.");
        }
    }

    private static string FirstMessage(JsonElement errors)
        => errors
            .EnumerateArray()
            .Select(error => AnimeThemesMapper.Field(error, "message"))
            .Select(text => text is { ValueKind: JsonValueKind.String } value ? value.GetString() : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
            ?? "no message";
}
