using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Web;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Persistent stores, retries, the web injection and older plugin copies.
/// </summary>
public class StoreTests
{
    [Fact]
    public void Problems_WaitLonger_EachTime()
    {
        Assert.Equal(TimeSpan.FromDays(1), FailedItemsStore.Backoff(ProblemKind.Unresolved, 1));
        Assert.Equal(TimeSpan.FromDays(3), FailedItemsStore.Backoff(ProblemKind.Unresolved, 2));
        Assert.Equal(TimeSpan.FromDays(30), FailedItemsStore.Backoff(ProblemKind.Unresolved, 9));
        Assert.Equal(TimeSpan.FromHours(1), FailedItemsStore.Backoff(ProblemKind.DownloadFailed, 1));
    }

    [Fact]
    public void Problems_AreRetried_WhenTheItemChanges()
    {
        using var temp = TestSupport.TempFolder();
        using var store = new FailedItemsStore(TestSupport.Paths(temp.Path), NullLogger<FailedItemsStore>.Instance);
        var series = new Series { Id = Guid.NewGuid(), Name = "Il prisma dell'amore" };

        store.Record(series, ProblemKind.Unresolved, "no entry");
        Assert.False(store.IsDue(series, DateTime.UtcNow));
        Assert.True(store.IsDue(series, DateTime.UtcNow.AddDays(2)));

        series.SetProviderId("AniList", "123");
        Assert.True(store.IsDue(series, DateTime.UtcNow));
    }

    [Fact]
    public void Problems_SurviveARestart_AndRead1xFiles()
    {
        using var temp = TestSupport.TempFolder();
        temp.File(
            "KometaThemes/failed-items.json",
            """[{"itemId":"ea9f78e5-9766-5ca8-4796-cc2cbb2bf181","name":"Good Night World","reason":"Unresolved","lastAttemptUtc":"2026-10-06T09:03:01Z","attempts":209}]""");

        using (var store = new FailedItemsStore(TestSupport.Paths(temp.Path), NullLogger<FailedItemsStore>.Instance))
        {
            var entry = store.Get(Guid.Parse("ea9f78e5-9766-5ca8-4796-cc2cbb2bf181"));
            Assert.NotNull(entry);
            Assert.Equal(0, entry!.Attempts);
            store.Record(new Series { Id = Guid.Parse("ea9f78e5-9766-5ca8-4796-cc2cbb2bf181"), Name = "Good Night World" }, ProblemKind.Unresolved, "still nothing");
        }

        using var reloaded = new FailedItemsStore(TestSupport.Paths(temp.Path), NullLogger<FailedItemsStore>.Instance);
        Assert.Equal("still nothing", reloaded.Get(Guid.Parse("ea9f78e5-9766-5ca8-4796-cc2cbb2bf181"))!.Error);
    }

    [Fact]
    public void Cache_StoresAnimeAndText_AndDrops1xFile()
    {
        using var temp = TestSupport.TempFolder();
        var legacy = temp.File("KometaThemes/resolution-cache.json", "{}");
        var anime = TestSupport.Fixture("kaguya-s1");

        using (var cache = new JsonResolutionCache(TestSupport.Paths(temp.Path), NullLogger<JsonResolutionCache>.Instance))
        {
            Assert.False(File.Exists(legacy));
            cache.SetPositive("AniList:101921", [anime]);
            cache.SetNegative("AniList:1");
            cache.SetText("anilist-chain:101921", "[1,2]");

            Assert.True(cache.TryGet("AniList:101921", out var found));
            Assert.Equal(anime.Id, found![0].Id);
            Assert.True(cache.TryGet("AniList:1", out var missing));
            Assert.Null(missing);
            Assert.True(cache.TryGetText("anilist-chain:101921", out var text));
            Assert.Equal("[1,2]", text);
            cache.Remove("AniList:1");
            Assert.False(cache.TryGet("AniList:1", out _));
        }

        using var reloaded = new JsonResolutionCache(TestSupport.Paths(temp.Path), NullLogger<JsonResolutionCache>.Instance);
        Assert.True(reloaded.TryGet("AniList:101921", out var again));
        Assert.Equal("Love Dramatic feat. Rikka Ihara", again![0].Themes![0].Song!.Title);
    }

    [Fact]
    public void Activity_KeepsNewestFirst()
    {
        using var temp = TestSupport.TempFolder();
        using var log = new ActivityLog(TestSupport.Paths(temp.Path), NullLogger<ActivityLog>.Instance);
        var id = Guid.NewGuid();
        log.Add(ActivityKind.Downloaded, id, "A", "first");
        log.Add(ActivityKind.Removed, Guid.NewGuid(), "B", "second");

        Assert.Equal(["second", "first"], log.Latest(10).Select(e => e.Message));
        Assert.Equal("first", Assert.Single(log.Latest(10, id)).Message);
    }

    [Theory]
    [InlineData("<html><body><div></div></body></html>", true)]
    [InlineData("<html><body></body></html><script id=\"kometathemes-item-button\"></script>", false)]
    [InlineData("var chunk = 1;", false)]
    public void IndexInjection_AddsTheScriptOnce(string page, bool changes)
    {
        var result = IndexInjection.TransformIndex(new IndexPayload { Contents = page });

        Assert.Equal(changes, !string.Equals(result, page, StringComparison.Ordinal));
        if (changes)
        {
            Assert.Contains("src=\"../KometaThemes/ItemButton.js?v=", result, StringComparison.Ordinal);
            Assert.EndsWith("</script></body></html>", result, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OlderCopies_AreSuperseded_OthersLeftAlone()
    {
        using var temp = TestSupport.TempFolder();
        var id = Guid.Parse("48c98707-45d1-43ac-94b8-f74d875ad29c");
        temp.File("KometaThemes_1.4.1.0/meta.json", $$"""{"guid":"{{id}}","version":"1.4.1.0","status":"Active","name":"KometaThemes"}""");
        temp.File("KometaThemes_2.0.0.0/meta.json", $$"""{"guid":"{{id}}","version":"2.0.0.0","status":"Active"}""");
        temp.File("Other_1.0.0.0/meta.json", """{"guid":"00000000-0000-0000-0000-000000000001","version":"1.0.0.0","status":"Active"}""");
        temp.File("KometaThemes_1.3.0.0/meta.json", $$"""{"guid":"{{id}}","version":"1.3.0.0","status":"Disabled"}""");

        var changed = PluginVersions.SupersedeOlderCopies(temp.Path, id, new Version(2, 0, 0, 0), Path.Combine(temp.Path, "KometaThemes_2.0.0.0"));

        Assert.Equal("KometaThemes_1.4.1.0", Path.GetFileName(Assert.Single(changed)));
        Assert.Contains("Superseded", File.ReadAllText(Path.Combine(temp.Path, "KometaThemes_1.4.1.0", "meta.json")), StringComparison.Ordinal);
        Assert.Contains("Disabled", File.ReadAllText(Path.Combine(temp.Path, "KometaThemes_1.3.0.0", "meta.json")), StringComparison.Ordinal);
    }
}
