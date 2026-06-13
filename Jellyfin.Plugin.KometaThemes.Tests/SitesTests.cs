using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Api;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class SitesTests
{
    [Fact]
    public void DefaultPriority_Contains_AllProviders()
    {
        Assert.Equal(5, Sites.DefaultPriority.Length);
        Assert.Contains("AniDB", Sites.DefaultPriority);
        Assert.Contains("AniList", Sites.DefaultPriority);
        Assert.Contains("MyAnimeList", Sites.DefaultPriority);
        Assert.Contains("Kitsu", Sites.DefaultPriority);
        Assert.Contains("AniSearch", Sites.DefaultPriority);
    }

    [Fact]
    public void NormalizeProviderPriority_Appends_Missing_Defaults()
    {
        var input = new List<string> { "AniList" };
        var result = Sites.NormalizeProviderPriority(input);
        Assert.Equal(5, result.Count);
        Assert.Equal("AniList", result[0]);
        Assert.Contains("AniDB", result);
    }

    [Fact]
    public void NormalizeProviderPriority_Ignores_Unknowns()
    {
        var input = new List<string> { "AniList", "garbage-provider" };
        var result = Sites.NormalizeProviderPriority(input);
        Assert.DoesNotContain("garbage-provider", result);
    }

    [Fact]
    public void NormalizeProviderPriority_Deduplicates()
    {
        var input = new List<string> { "AniList", "AniList", "AniDB" };
        var result = Sites.NormalizeProviderPriority(input);
        Assert.Equal(5, result.Distinct().Count());
    }

    [Fact]
    public void NormalizeProviderPriority_Collapses_AccumulatedDuplicates()
    {
        // Regression: the XmlSerializer collection-append bug grew the saved list by
        // the full default sequence on every load/save cycle (observed: 35 = 5 x 7).
        // Normalization must collapse it back to exactly the five canonical providers
        // in first-seen order.
        var bloated = Enumerable.Range(0, 7).SelectMany(_ => Sites.DefaultPriority).ToList();
        Assert.Equal(35, bloated.Count);

        var result = Sites.NormalizeProviderPriority(bloated);

        Assert.Equal(Sites.DefaultPriority, result);
    }
}
