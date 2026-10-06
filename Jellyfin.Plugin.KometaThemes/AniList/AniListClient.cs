using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.AniList;

/// <summary>
/// Reads the sequel chain of an anime from AniList, which is how a later season finds its own entry.
/// </summary>
public sealed class AniListClient
{
    /// <summary>Name of the configured HTTP client.</summary>
    public const string HttpClientName = "AniList";

    private const string MediaQuery = """
        query ($id: Int) {
          Media(id: $id, type: ANIME) {
            id idMal format episodes
            startDate { year month }
            title { romaji english }
            relations { edges { relationType node { id type format episodes startDate { year month } title { romaji english } } } }
          }
        }
        """;

    /// <summary>Formats that count as a season of a series.</summary>
    private static readonly string[] SeasonFormats = ["TV", "TV_SHORT", "ONA"];

    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<AniListClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AniListClient"/> class.
    /// </summary>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public AniListClient(IHttpClientFactory clientFactory, ILogger<AniListClient> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Follows SEQUEL relations from an anime, keeping only entries that are a season of a series.
    /// </summary>
    /// <param name="rootId">AniList ID of the first season.</param>
    /// <param name="maxLength">Longest chain to build, root included.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chain starting with the root; only the root when AniList does not know it.</returns>
    /// <exception cref="HttpRequestException">AniList could not be reached.</exception>
    public async Task<IReadOnlyList<AniListSeason>> GetSequelChainAsync(int rootId, int maxLength, CancellationToken cancellationToken)
    {
        var chain = new List<AniListSeason>();
        var seen = new HashSet<int>();
        int? next = rootId;

        while (next is { } id && chain.Count < maxLength && seen.Add(id))
        {
            var media = await GetMediaAsync(id, cancellationToken).ConfigureAwait(false);
            if (media == null)
            {
                break;
            }

            chain.Add(ToSeason(media));
            next = PickSequel(media);
        }

        _logger.LogDebug("AniList sequel chain from {Root}: {Chain}", rootId, string.Join(" > ", chain.Select(s => s.Id)));
        return chain;
    }

    /// <summary>
    /// Picks the sequel that continues the series: a TV-like format, earliest start first.
    /// </summary>
    /// <param name="media">The current entry.</param>
    /// <returns>The next entry's ID, or null.</returns>
    internal static int? PickSequel(AniListMedia media)
        => (media.Relations?.Edges ?? [])
            .Where(edge => string.Equals(edge.RelationType, "SEQUEL", StringComparison.Ordinal)
                && edge.Node != null
                && string.Equals(edge.Node.Type, "ANIME", StringComparison.Ordinal)
                && SeasonFormats.Contains(edge.Node.Format, StringComparer.Ordinal))
            .OrderBy(edge => Array.IndexOf(SeasonFormats, edge.Node!.Format) == 2 ? 1 : 0)
            .ThenBy(edge => edge.Node!.StartDate?.Year ?? int.MaxValue)
            .ThenBy(edge => edge.Node!.StartDate?.Month ?? 13)
            .Select(edge => (int?)edge.Node!.Id)
            .FirstOrDefault();

    private static AniListSeason ToSeason(AniListMedia media)
        => new(media.Id, media.Format, media.Episodes, media.StartDate?.Year, media.StartDate?.Month, media.Title?.English ?? media.Title?.Romaji);

    private async Task<AniListMedia?> GetMediaAsync(int id, CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient(HttpClientName);
        var request = new GraphQlRequest(MediaQuery, new Dictionary<string, int> { ["id"] = id });
        using var response = await client.PostAsJsonAsync(string.Empty, request, cancellationToken).ConfigureAwait(false);

        GraphQlResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<GraphQlResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("AniList sent a response that is not valid JSON.", ex);
        }

        if (payload?.Data?.Media is { } media)
        {
            return media;
        }

        // GraphQL reports "not found" as 404 with an errors array; anything else non-successful is an outage.
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 404)
        {
            throw new HttpRequestException($"AniList answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        _logger.LogDebug("AniList has no anime {Id}: {Error}", id, payload?.Errors is { Count: > 0 } errors ? errors[0].Message : null);
        return null;
    }

    private sealed record GraphQlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, int> Variables);

    private sealed record GraphQlResponse(
        [property: JsonPropertyName("data")] GraphQlData? Data,
        [property: JsonPropertyName("errors")] IReadOnlyList<GraphQlError>? Errors);

    private sealed record GraphQlData([property: JsonPropertyName("Media")] AniListMedia? Media);

    private sealed record GraphQlError([property: JsonPropertyName("message")] string? Message);
}

/// <summary>
/// An AniList anime as returned by the media query.
/// </summary>
/// <param name="Id">AniList ID.</param>
/// <param name="Type">Media type, <c>ANIME</c> or <c>MANGA</c>.</param>
/// <param name="Format">Format such as <c>TV</c> or <c>MOVIE</c>.</param>
/// <param name="Episodes">Episode count, when known.</param>
/// <param name="StartDate">Premiere date.</param>
/// <param name="Title">Titles.</param>
/// <param name="Relations">Related entries.</param>
public sealed record AniListMedia(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("episodes")] int? Episodes,
    [property: JsonPropertyName("startDate")] AniListDate? StartDate,
    [property: JsonPropertyName("title")] AniListTitle? Title,
    [property: JsonPropertyName("relations")] AniListRelations? Relations);

/// <summary>An AniList fuzzy date.</summary>
/// <param name="Year">Year.</param>
/// <param name="Month">Month.</param>
public sealed record AniListDate([property: JsonPropertyName("year")] int? Year, [property: JsonPropertyName("month")] int? Month);

/// <summary>AniList titles.</summary>
/// <param name="Romaji">Romaji title.</param>
/// <param name="English">English title.</param>
public sealed record AniListTitle([property: JsonPropertyName("romaji")] string? Romaji, [property: JsonPropertyName("english")] string? English);

/// <summary>AniList relations.</summary>
/// <param name="Edges">Relation edges.</param>
public sealed record AniListRelations([property: JsonPropertyName("edges")] IReadOnlyList<AniListEdge>? Edges);

/// <summary>One AniList relation.</summary>
/// <param name="RelationType">Relation, such as <c>SEQUEL</c>.</param>
/// <param name="Node">The related entry.</param>
public sealed record AniListEdge([property: JsonPropertyName("relationType")] string? RelationType, [property: JsonPropertyName("node")] AniListMedia? Node);

/// <summary>
/// One link of a sequel chain.
/// </summary>
/// <param name="Id">AniList ID.</param>
/// <param name="Format">Format.</param>
/// <param name="Episodes">Episode count, when known.</param>
/// <param name="Year">Premiere year, when known.</param>
/// <param name="Month">Premiere month, when known.</param>
/// <param name="Title">Display title.</param>
public sealed record AniListSeason(int Id, string? Format, int? Episodes, int? Year, int? Month, string? Title);
