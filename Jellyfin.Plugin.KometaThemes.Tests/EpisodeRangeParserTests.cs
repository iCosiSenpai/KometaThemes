using Jellyfin.Plugin.KometaThemes.Resolving;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The episode lists animethemes.moe writes.
/// </summary>
public class EpisodeRangeParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrNull_ReturnsEmpty(string? input)
    {
        var result = EpisodeRangeParser.Parse(input);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_SingleEpisode()
    {
        var result = EpisodeRangeParser.Parse("5");
        Assert.Single(result);
        Assert.Equal(5, result[0].StartEpisode);
        Assert.Equal(5, result[0].EndEpisode);
    }

    [Fact]
    public void Parse_Range()
    {
        var result = EpisodeRangeParser.Parse("1-3");
        Assert.Single(result);
        Assert.Equal(1, result[0].StartEpisode);
        Assert.Equal(3, result[0].EndEpisode);
    }

    [Fact]
    public void Parse_MultipleCommaSeparated()
    {
        var result = EpisodeRangeParser.Parse("1, 5-7, 10");
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].StartEpisode);
        Assert.Equal(5, result[1].StartEpisode);
        Assert.Equal(10, result[2].StartEpisode);
    }

    [Fact]
    public void Parse_InvalidPartsAreIgnored()
    {
        var result = EpisodeRangeParser.Parse("abc, 1-2, xyz-5, 3");
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].StartEpisode);
        Assert.Equal(3, result[1].StartEpisode);
    }

    [Fact]
    public void Parse_NegativeIgnored()
    {
        var result = EpisodeRangeParser.Parse("-1, 2-4");
        Assert.Single(result);
        Assert.Equal(2, result[0].StartEpisode);
    }

    [Fact]
    public void Parse_ReversedRangeIgnoredEnd()
    {
        var result = EpisodeRangeParser.Parse("5-3");
        Assert.Single(result);
        Assert.Equal(5, result[0].StartEpisode);
        Assert.Equal(5, result[0].EndEpisode);
    }

    [Fact]
    public void Parse_Deduplicates()
    {
        var result = EpisodeRangeParser.Parse("1-2,1-2,3");
        Assert.Equal(2, result.Count);
    }
}
