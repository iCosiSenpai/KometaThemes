using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.AniList;

/// <summary>
/// Reads the sequels of an anime from AniList. Optional: season matching works without it.
/// </summary>
/// <remarks>
/// <para>
/// 2.0 followed sequels only through TV entries, so a chain stopped at the first movie or OVA: Sword
/// Art Online never reached Alicization (after the Ordinal Scale movie), KonoSuba never reached its
/// third season (after the Crimson Legend movie). Sequels are now followed through every format, and
/// the season matcher decides which of them are seasons.
/// </para>
/// <para>
/// AniList can switch its API off entirely ("temporarily disabled due to severe stability issues",
/// answered with 403, as on 2026-10-21). After such an answer no request is sent for an hour.
/// </para>
/// </remarks>
public sealed class AniListClient
{
    /// <summary>Name of the configured HTTP client.</summary>
    public const string HttpClientName = "AniList";

    private const string MediaQuery = """
        query ($id: Int) {
          Media(id: $id, type: ANIME) {
            id format episodes
            startDate { year month }
            title { romaji english }
            relations { edges { relationType node { id type format episodes startDate { year month } title { romaji english } } } }
          }
        }
        """;

    private static readonly TimeSpan PauseAfterRefusal = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<AniListClient> _logger;
    private DateTime _pausedUntilUtc = DateTime.MinValue;

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
    /// Follows SEQUEL relations from an anime through every format except music videos.
    /// </summary>
    /// <param name="rootId">AniList ID of the first season.</param>
    /// <param name="maxEntries">Most entries to read, root included.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The root and its sequels in premiere order; only the root when AniList does not know it.</returns>
    /// <exception cref="HttpRequestException">AniList could not be reached or refused the request.</exception>
    public async Task<IReadOnlyList<AniListSeason>> GetSequelChainAsync(int rootId, int maxEntries, CancellationToken cancellationToken)
    {
        var found = new List<AniListSeason>();
        var queued = new HashSet<int> { rootId };
        var queue = new Queue<int>();
        queue.Enqueue(rootId);

        while (queue.Count > 0 && found.Count < maxEntries)
        {
            var media = await GetMediaAsync(queue.Dequeue(), cancellationToken).ConfigureAwait(false);
            if (media == null)
            {
                continue;
            }

            found.Add(ToSeason(media));
            foreach (var next in Sequels(media).Where(queued.Add))
            {
                queue.Enqueue(next);
            }
        }

        _logger.LogDebug("AniList sequels of {Root}: {Chain}", rootId, string.Join(", ", found.Select(s => s.Id)));
        return found
            .OrderBy(s => s.Year ?? int.MaxValue)
            .ThenBy(s => s.Month ?? 13)
            .ThenBy(s => s.Id)
            .ToList();
    }

    /// <summary>
    /// Lists the sequels of an entry, earliest first.
    /// </summary>
    /// <param name="media">The entry.</param>
    /// <returns>AniList IDs.</returns>
    internal static IEnumerable<int> Sequels(AniListMedia media)
        => (media.Relations?.Edges ?? [])
            .Where(edge => string.Equals(edge.RelationType, "SEQUEL", StringComparison.Ordinal)
                && edge.Node is { Type: "ANIME" }
                && !string.Equals(edge.Node.Format, "MUSIC", StringComparison.Ordinal))
            .OrderBy(edge => edge.Node!.StartDate?.Year ?? int.MaxValue)
            .ThenBy(edge => edge.Node!.StartDate?.Month ?? 13)
            .Select(edge => edge.Node!.Id);

    private static AniListSeason ToSeason(AniListMedia media)
        => new(media.Id, media.Format, media.Episodes, media.StartDate?.Year, media.StartDate?.Month, media.Title?.English ?? media.Title?.Romaji);

    private async Task<AniListMedia?> GetMediaAsync(int id, CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _pausedUntilUtc)
        {
            throw new HttpRequestException("AniList refused requests recently; trying again later.", null, HttpStatusCode.Forbidden);
        }

        var client = _clientFactory.CreateClient(HttpClientName);
        var request = new GraphQlRequest(MediaQuery, new Dictionary<string, int> { ["id"] = id });
        using var response = await client.PostAsJsonAsync(string.Empty, request, cancellationToken).ConfigureAwait(false);

        GraphQlResponse? payload = null;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<GraphQlResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            if (response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("AniList sent a response that is not valid JSON.", ex);
            }
        }

        if (payload?.Data?.Media is { } media)
        {
            return media;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _pausedUntilUtc = DateTime.UtcNow + PauseAfterRefusal;
            var reason = payload?.Errors is { Count: > 0 } refused ? refused[0].Message : null;
            _logger.LogWarning("AniList refused the request ({Reason}); season matching goes on without it for an hour", reason ?? "403");
            throw new HttpRequestException("AniList refused the request: " + (reason ?? "403"), null, response.StatusCode);
        }

        // GraphQL reports "not found" as 404 with an errors array; anything else non-successful is an outage.
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
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
/// One entry of a sequel chain.
/// </summary>
/// <param name="Id">AniList ID.</param>
/// <param name="Format">Format.</param>
/// <param name="Episodes">Episode count, when known.</param>
/// <param name="Year">Premiere year, when known.</param>
/// <param name="Month">Premiere month, when known.</param>
/// <param name="Title">Display title.</param>
public sealed record AniListSeason(int Id, string? Format, int? Episodes, int? Year, int? Month, string? Title);
