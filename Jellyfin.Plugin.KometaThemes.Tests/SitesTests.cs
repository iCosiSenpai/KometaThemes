using Jellyfin.Plugin.KometaThemes.Api;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The external sites animethemes.moe can look anime up by.
/// </summary>
public class SitesTests
{
    [Fact]
    public void DefaultPriority_HasTheFourSitesAnimeThemesKnows()
    {
        Assert.Equal(["AniDB", "AniList", "MyAnimeList", "Kitsu"], Sites.DefaultPriority);
        Assert.Equal(Sites.DefaultPriority.Count, Sites.ProviderToSite.Count);
    }

    [Fact]
    public void NormalizeProviderPriority_AppendsMissingDefaults()
    {
        var result = Sites.NormalizeProviderPriority(["AniList"]);

        Assert.Equal(4, result.Count);
        Assert.Equal("AniList", result[0]);
        Assert.Contains("AniDB", result);
    }

    [Fact]
    public void NormalizeProviderPriority_DropsAniSearchAndUnknowns()
    {
        // aniSearch was offered by 2.0, but animethemes.moe stores no aniSearch IDs.
        var result = Sites.NormalizeProviderPriority(["AniSearch", "garbage-provider", "kitsu"]);

        Assert.Equal(["Kitsu", "AniDB", "AniList", "MyAnimeList"], result);
    }

    [Fact]
    public void NormalizeProviderPriority_CollapsesAccumulatedDuplicates()
    {
        // The XmlSerializer collection-append bug of 1.0 grew the saved list on every load.
        var bloated = Enumerable.Range(0, 7).SelectMany(_ => new[] { "AniDB", "AniList", "MyAnimeList", "Kitsu", "AniSearch" }).ToList();

        Assert.Equal(Sites.DefaultPriority, Sites.NormalizeProviderPriority(bloated));
    }
}
