using System.Net;
using System.Text;
using Jellyfin.Plugin.KometaThemes.AnimeThemes;
using Jellyfin.Plugin.KometaThemes.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Retry handling and the animethemes.moe client, against a fake server.
/// </summary>
public class HttpTests
{
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> _answer;

        public FakeServer(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> answer) => _answer = (r, c, _) => answer(r, c);

        public FakeServer(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

        public int Calls { get; private set; }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return _answer(request, ++Calls, cancellationToken);
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
            .Returns(() => new HttpClient(server, disposeHandler: false) { BaseAddress = new Uri("https://api.animethemes.moe/") });
        return new AnimeThemesClient(factory.Object, NullLogger<AnimeThemesClient>.Instance);
    }

    [Fact]
    public async Task FindByExternalIds_FiltersAndRechecksTheLink()
    {
        var kaguya = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kaguya-s1.json"));
        var anime = System.Text.Json.JsonDocument.Parse(kaguya).RootElement.GetProperty("anime").GetRawText();
        var server = new FakeServer((_, _) => Task.FromResult(Json("{\"anime\":[" + anime + "],\"links\":{\"next\":null}}")));

        var result = await Client(server).FindByExternalIdsAsync("AniList", ["101921", "999", "not-a-number"], CancellationToken.None);

        var query = Uri.UnescapeDataString(server.Requests.Single().Query);
        Assert.Contains("filter[has]=resources", query, StringComparison.Ordinal);
        Assert.Contains("filter[resource][external_id]=101921,999", query, StringComparison.Ordinal);
        Assert.Single(result["101921"]);
        Assert.Empty(result["999"]);
        Assert.Empty(result["not-a-number"]);
    }

    [Fact]
    public async Task FindByExternalIds_ReportsAnOutage_InsteadOfNotFound()
    {
        var server = new FakeServer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(server).FindByExternalIdsAsync("AniList", ["1"], CancellationToken.None));
    }

    [Fact]
    public async Task GetAnime_ReturnsNullFor404_AndThrowsOnGarbage()
    {
        var missing = new FakeServer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await Client(missing).GetAnimeBySlugAsync("nope", CancellationToken.None));

        var garbage = new FakeServer((_, _) => Task.FromResult(Json("<html>maintenance</html>")));
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(garbage).GetAnimeAsync(1, CancellationToken.None));
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
