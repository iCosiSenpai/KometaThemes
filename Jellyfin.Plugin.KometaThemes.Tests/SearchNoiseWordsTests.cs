using System.Collections.Generic;
using Jellyfin.Plugin.KometaThemes.Search;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class SearchNoiseWordsTests
{
    [Theory]
    [InlineData("season")]
    [InlineData("the")]
    [InlineData("special")]
    public void SearchNoiseWords_Contains_Common(string word)
    {
        Assert.Contains(word, GetNoiseWords());
    }

    [Fact]
    public void SearchNoiseWords_Are_CaseInsensitive()
    {
        var set = GetNoiseWords();
        Assert.Contains("SEASON", set);
    }

    private static HashSet<string> GetNoiseWords()
    {
        var field = typeof(KometaThemesSearchController).GetField(
            "SearchNoiseWords",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        return (HashSet<string>)field!.GetValue(null)!;
    }
}
