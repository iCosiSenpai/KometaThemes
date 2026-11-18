using System.Net;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Http;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Retry handling and the animethemes.moe client, against a fake server.
/// </summary>
/// <remarks>
/// The responses come from the live GraphQL API and are kept in <c>Fixtures</c>: the REST API the
/// client used before answers 503 on every route, so a hand-written body would hide a schema change.
/// </remarks>
public class HttpTests
{
    private static readonly Uri BaseAddress = new("https://graphql.animethemes.moe/");

    /// <summary>One request the fake server received.</summary>
    private sealed record Sent(Uri Uri, HttpMethod Method, string Body);

    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _answer;

        public FakeServer(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> answer) => _answer = (r, c, _) => answer(r, c);

        public FakeServer(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

        public int Calls { get; private set; }

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Sent(request.RequestUri!, request.Method, body));
            return await _answer(request, ++Calls, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static RetryHandler Retry(HttpMessageHandler inner) => new(NullLogger<RetryHandler>.Instance)
    {
        InnerHandler = inner,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        AttemptTimeout = TimeSpan.FromMilliseconds(200),
    };

    [Fact]
    public async Task Retry_RecoversFromTransientErrors()
    {
        var server = new FakeServer((_, call) => Task.FromResult(call < 3 ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Json("{}")));
        using var client = new HttpClient(Retry(server));

        using var response = await client.GetAsync(new Uri("https://example.test/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, server.Calls);
    }

    [Fact]
    public async Task Retry_DoesNotRetryClientErrors()
    {
        var server = new FakeServer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = new HttpClient(Retry(server));

        using var response = await client.GetAsync(new Uri("https://example.test/"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public async Task Retry_TurnsTimeoutsIntoHttpRequestException()
    {
        // 1.x let Polly's TimeoutRejectedException escape, which no caller handled.
        var server = new FakeServer(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return Json("{}");
        });
        using var client = new HttpClient(Retry(server));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("https://example.test/")));
        Assert.Equal(4, server.Calls);
    }

    [Fact]
    public async Task Retry_StopsWhenTheCallerCancels()
    {
        var server = new FakeServer(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return Json("{}");
        });
        using var client = new HttpClient(Retry(server));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(new Uri("https://example.test/"), cts.Token));
        Assert.Equal(1, server.Calls);
    }

    private static AnimeThemesClient Client(HttpMessageHandler server)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AnimeThemesClient.HttpClientName))
            .Returns(() => new HttpClient(server, disposeHandler: false) { BaseAddress = BaseAddress });
        return new AnimeThemesClient(factory.Object, NullLogger<AnimeThemesClient>.Instance);
    }

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>The anime of the recorded detail response, as the API sent it.</summary>
    private static string AnimeNode()
    {
        using var document = JsonDocument.Parse(Fixture("graphql-bocchi.json"));
        return document.RootElement
            .GetProperty("data")
            .GetProperty("animePagination")
            .GetProperty("data")
            .EnumerateArray()
            .Single()
            .GetRawText();
    }

    /// <summary>The GraphQL request the client sent: the document and its variables.</summary>
    private static (string Query, JsonElement Variables) GraphQl(Sent sent)
    {
        using var document = JsonDocument.Parse(sent.Body);
        var root = document.RootElement;
        return (root.GetProperty("query").GetString()!, root.GetProperty("variables").Clone());
    }

    private static int[] Numbers(JsonElement array) => array.EnumerateArray().Select(item => item.GetInt32()).ToArray();

    [Fact]
    public async Task FindByExternalIds_FindsTheAniListId()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"findAnimeByExternalSite\":[" + AnimeNode() + "]}}")));

        var result = await Client(server).FindByExternalIdsAsync("AniList", ["130003", "999", "not-a-number"], CancellationToken.None);

        var sent = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(BaseAddress, sent.Uri);
        var (query, variables) = GraphQl(sent);
        Assert.Contains("findAnimeByExternalSite", query, StringComparison.Ordinal);
        Assert.Equal("ANILIST", variables.GetProperty("site").GetString());
        Assert.Equal([130003, 999], Numbers(variables.GetProperty("ids")));

        var anime = Assert.Single(result["130003"]);
        Assert.Equal(3912, anime.Id);
        Assert.Equal("bocchi_the_rock", anime.Slug);
        Assert.Equal(130003, anime.GetExternalId("AniList"));
        Assert.Empty(result["999"]);
        Assert.Empty(result["not-a-number"]);
    }

    [Fact]
    public async Task FindByExternalIds_DoesNotAskAboutASiteItCannotMatch()
    {
        // aniSearch was the 1.x bug: animethemes.moe stores no such site, so every ID came back empty.
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"findAnimeByExternalSite\":[]}}")));

        var result = await Client(server).FindByExternalIdsAsync("aniSearch", ["1"], CancellationToken.None);

        Assert.Empty(result["1"]);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task FindByExternalIds_ReportsAnOutage_InsteadOfNotFound()
    {
        var server = new FakeServer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).FindByExternalIdsAsync("AniList", ["1"], CancellationToken.None));
    }

    [Fact]
    public async Task GetAnime_MapsTheDetailTheRestIncludeProvided()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"a0\":{\"data\":[" + AnimeNode() + "]}}}")));

        var found = await Client(server).GetAnimeAsync(3912, CancellationToken.None);

        var sent = Assert.Single(server.Requests);
        var (query, variables) = GraphQl(sent);
        Assert.Contains("animePagination", query, StringComparison.Ordinal);
        Assert.Contains("animethemeentries", query, StringComparison.Ordinal);
        Assert.Equal(3912, variables.GetProperty("id0").GetInt32());

        Assert.NotNull(found);
        Assert.Equal("Bocchi the Rock!", found!.Name);
        Assert.Equal(2022, found.Year);

        // The REST API spelled them "Fall" and "TV"; SeasonMatcher and TitleMatcher compare those words.
        Assert.Equal("Fall", found.Season);
        Assert.Equal("TV", found.MediaFormat);

        Assert.NotNull(found.GetCover(large: false));
        Assert.NotNull(found.GetCover(large: true));
        Assert.Equal(47917, found.GetExternalId("MyAnimeList"));
        Assert.Equal(44196, found.GetExternalId("Kitsu"));
        Assert.Equal("bocchi_the_rock", Assert.Single(found.Series!).Slug);

        var opening = Assert.Single(found.Themes!, theme => theme.Type == ThemeType.OP);
        Assert.Equal("OP1", opening.Slug);
        Assert.Equal(11829, opening.Id);
        Assert.Equal("Seishun Complex", opening.Song!.Title);
        Assert.Equal("Kessoku Band", opening.Song.ArtistNames);
        Assert.Equal("kessoku_band", Assert.Single(opening.Song.Artists!).Slug);
        Assert.Equal("1-7, 9-11", Assert.Single(opening.Entries!).Episodes);

        var ending = Assert.Single(found.Themes!, theme => theme.Type == ThemeType.ED);
        Assert.Equal("ED1", ending.Slug);
        Assert.Equal(1, ending.Sequence);

        var video = opening.Entries!.Single().Videos!.Single(item => item.Creditless);
        Assert.Equal(1080, video.Resolution);
        Assert.Equal(VideoSource.BD, video.Source);
        Assert.Equal(OverlapType.None, video.Overlap);
        Assert.Equal("BocchiTheRock-OP1-NCBD1080.webm", video.Basename);
        Assert.Equal(55992110L, video.Size);
        Assert.False(video.Subbed);
        Assert.False(video.Lyrics);
        Assert.Equal("https://a.animethemes.moe/BocchiTheRock-OP1-NCBD1080.ogg", video.Audio!.Link);
        Assert.Equal(3619755L, video.Audio.Size);
    }

    [Fact]
    public async Task GetAnime_BatchesTheIdsIntoOneRequest()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"a0\":{\"data\":[" + AnimeNode() + "]},\"a1\":{\"data\":[]}}}")));

        var found = await Client(server).GetAnimeAsync([3912, 3913], CancellationToken.None);

        var sent = Assert.Single(server.Requests);
        var (_, variables) = GraphQl(sent);
        Assert.Equal(3912, variables.GetProperty("id0").GetInt32());
        Assert.Equal(3913, variables.GetProperty("id1").GetInt32());
        Assert.Equal(3912, Assert.Single(found).Id);
    }

    [Fact]
    public async Task GetAnime_DropsAnimeThatWereNotAskedFor()
    {
        // An ignored filter must never hand back unrelated anime.
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"a0\":{\"data\":[" + AnimeNode() + "]}}}")));

        Assert.Null(await Client(server).GetAnimeAsync(42, CancellationToken.None));
    }

    [Fact]
    public async Task GetSeries_ReturnsTheFranchiseMembers()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json(Fixture("graphql-series-bocchi.json"))));

        var members = await Client(server).GetSeriesAsync("bocchi_the_rock", CancellationToken.None);

        var sent = Assert.Single(server.Requests);
        var (query, variables) = GraphQl(sent);
        Assert.Contains("series", query, StringComparison.Ordinal);
        Assert.Equal("bocchi_the_rock", variables.GetProperty("slug").GetString());

        Assert.Equal([3912, 4517, 4518], members.Select(member => member.Id).Order());
        var television = members.Single(member => member.Id == 3912);
        Assert.Equal("Bocchi the Rock!", television.Name);
        Assert.Equal("TV", television.MediaFormat);
        Assert.Equal("Fall", television.Season);

        // Members come without their themes: SeasonResolver fetches the details of the entries it chooses.
        var movie = members.Single(member => member.Id == 4517);
        Assert.Equal("Movie", movie.MediaFormat);
        Assert.Equal("Spring", movie.Season);
        Assert.Null(movie.Themes);
    }

    [Fact]
    public async Task GetSeries_ReturnsEmptyForAnUnknownSlug()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"series\":null}}")));

        Assert.Empty(await Client(server).GetSeriesAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task Search_ReturnsTheRankedMatches()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json(Fixture("graphql-search-takagi.json"))));

        var results = await Client(server).SearchAsync("Takagi-san", CancellationToken.None);

        var sent = Assert.Single(server.Requests);
        var (query, variables) = GraphQl(sent);
        Assert.Contains("search", query, StringComparison.Ordinal);
        Assert.Equal("Takagi-san", variables.GetProperty("search").GetString());

        Assert.Equal([4006, 3624, 1449, 1448], results.Select(anime => anime.Id));
        var television = results.Single(anime => anime.Id == 1448);
        Assert.Equal("Karakai Jouzu no Takagi-san", television.Name);
        Assert.Equal("TV", television.MediaFormat);
        Assert.Equal("Winter", television.Season);
        Assert.Contains("Skilled Teaser Takagi-san", television.Titles);
        Assert.Equal("karakai_jouzu_no_takagi_san", Assert.Single(television.Series!).Slug);
        Assert.NotNull(television.GetCover(large: false));
        Assert.Contains(television.Themes!, theme => theme.Slug == "OP1" && theme.Type == ThemeType.OP);
    }

    [Fact]
    public async Task Search_ReturnsNothingForBlankText()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"search\":{\"anime\":[]}}}")));

        Assert.Empty(await Client(server).SearchAsync("   ", CancellationToken.None));
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Client_ReturnsEmptyForAnAnswerWithoutData()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"data\":{\"findAnimeByExternalSite\":[],\"animePagination\":{\"data\":[]},\"search\":{\"anime\":[]}}}")));

        var found = await Client(server).FindByExternalIdsAsync("AniList", ["1"], CancellationToken.None);
        Assert.Empty(found["1"]);
        Assert.Null(await Client(server).GetAnimeAsync(1, CancellationToken.None));
        Assert.Empty(await Client(server).SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task GetAnime_TranslatesTheGraphQlEnumSpellings()
    {
        // 2.x compares "TV Short", "Winter", "Transition" and "Over", and skips anything but OP or ED.
        const string body = """
            {
              "data": {
                "a0": {
                  "data": [
                    {
                      "id": 7,
                      "slug": "short",
                      "year": 2021,
                      "season": "WINTER",
                      "format": "TV_SHORT",
                      "title": {
                        "romaji": "Short"
                      },
                      "animethemes": [
                        {
                          "id": 70,
                          "type": "IN",
                          "sequence": null,
                          "slug": "IN1"
                        },
                        {
                          "id": 71,
                          "type": "ED",
                          "sequence": 2,
                          "slug": "ED2",
                          "song": {
                            "id": 710,
                            "title": {
                              "romaji": "Song",
                              "native": null
                            },
                            "performances": []
                          },
                          "animethemeentries": [
                            {
                              "id": 7100,
                              "version": 2,
                              "episodes": null,
                              "nsfw": true,
                              "spoiler": true,
                              "videos": {
                                "nodes": [
                                  {
                                    "id": 71000,
                                    "basename": "Short-ED2.webm",
                                    "link": "https://v.animethemes.moe/Short-ED2.webm",
                                    "resolution": 720,
                                    "source": "DVD",
                                    "overlap": "TRANS",
                                    "nc": false,
                                    "subbed": true,
                                    "lyrics": true,
                                    "size": 1,
                                    "audio": {
                                      "id": 7100,
                                      "link": "https://a.animethemes.moe/Short-ED2.ogg",
                                      "size": 2
                                    }
                                  }
                                ]
                              }
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """;
        var server = new FakeServer((_, _) => Task.FromResult(Json(body)));

        var anime = await Client(server).GetAnimeAsync(7, CancellationToken.None);

        Assert.NotNull(anime);
        Assert.Equal("TV Short", anime!.MediaFormat);
        Assert.Equal("Winter", anime.Season);
        Assert.Contains(anime.Themes!, theme => theme.Type == ThemeType.Other && theme.Slug == "IN1");
        var ending = anime.Themes!.Single(theme => theme.Type == ThemeType.ED);

        // A song without credited performers has no artist list, not an empty name.
        Assert.Empty(ending.Song!.Artists ?? []);
        var entry = Assert.Single(ending.Entries!);
        Assert.True(entry.Nsfw);
        Assert.True(entry.Spoiler);
        Assert.Equal(2, entry.Version);
        var video = Assert.Single(entry.Videos!);
        Assert.Equal(VideoSource.DVD, video.Source);
        Assert.Equal(OverlapType.Transition, video.Overlap);
        Assert.True(video.Subbed);
        Assert.True(video.Lyrics);
        Assert.False(video.Creditless);
    }

    [Fact]
    public async Task Client_KeepsTheDataOfAPartialAnswer()
    {
        // One alias of a batch can fail while the others answer; the data that came back is still usable.
        var server = new FakeServer((_, _) => Task.FromResult(Json(
            "{\"data\":{\"a0\":{\"data\":[" + AnimeNode() + "]},\"a1\":null},\"errors\":[{\"message\":\"a1 failed\"}]}")));

        var found = await Client(server).GetAnimeAsync([3912, 3913], CancellationToken.None);

        Assert.Equal(3912, Assert.Single(found).Id);
    }

    [Fact]
    public async Task Client_TurnsGraphQlErrorsIntoHttpRequestException()
    {
        // A request the API refuses was not answered: the caller must not read it as "not on animethemes.moe".
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"errors\":[{\"message\":\"Cannot query field\"}]}")));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).GetAnimeAsync(1, CancellationToken.None));
        Assert.Contains("Cannot query field", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).SearchAsync("anything", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).GetSeriesAsync("anything", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Client_TurnsANonSuccessStatusIntoHttpRequestException(HttpStatusCode status)
    {
        var server = new FakeServer((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).GetSeriesAsync("bocchi_the_rock", CancellationToken.None));
    }

    [Fact]
    public async Task Client_TurnsAnUnreadableBodyIntoHttpRequestException()
    {
        var server = new FakeServer((_, _) => Task.FromResult(Json("<html>maintenance</html>")));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).GetAnimeAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Throttle_PacesRequests()
    {
        using var throttle = new ApiThrottle("test", () => 600);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 14; i++)
        {
            await throttle.WaitAsync(CancellationToken.None);
        }

        // 10 burst tokens, then one every 100 ms.
        Assert.True(watch.ElapsedMilliseconds >= 300, $"took {watch.ElapsedMilliseconds} ms");
    }
}
