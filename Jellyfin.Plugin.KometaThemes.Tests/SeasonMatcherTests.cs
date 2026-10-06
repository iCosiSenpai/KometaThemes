using Jellyfin.Plugin.KometaThemes.AniList;
using Jellyfin.Plugin.KometaThemes.Resolving;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The season matcher against real AniList chains.
/// </summary>
public class SeasonMatcherTests
{
    // Kaguya-sama wa Kokurasetai as AniList lists it: four TV entries.
    private static readonly AniListSeason[] Kaguya =
    [
        new(101921, "TV", 12, 2019, 1, "Kaguya-sama: Love is War"),
        new(112641, "TV", 12, 2020, 4, "Kaguya-sama: Love is War?"),
        new(125367, "TV", 13, 2022, 4, "Kaguya-sama: Love is War -Ultra Romantic-"),
        new(151384, "TV", 4, 2023, 4, "Kaguya-sama: Love is War -The First Kiss That Never Ends-"),
    ];

    // Attack on Titan: TVDB puts both parts of season 3 into one season.
    private static readonly AniListSeason[] Titan =
    [
        new(16498, "TV", 25, 2013, 4, "Attack on Titan"),
        new(20958, "TV", 12, 2017, 4, "Attack on Titan Season 2"),
        new(99147, "TV", 12, 2018, 7, "Attack on Titan Season 3"),
        new(104578, "TV", 10, 2019, 4, "Attack on Titan Season 3 Part 2"),
        new(110277, "TV", 16, 2020, 12, "Attack on Titan Final Season"),
    ];

    [Fact]
    public void Kaguya_Season2_IsTheSecondSeries_NotTheFirstSeasonsEnding()
    {
        var match = SeasonMatcher.Match(Kaguya, new LibrarySeason(2, 2020, 4, 12));

        Assert.NotNull(match);
        Assert.Equal(112641, Assert.Single(match!.Entries).Id);
        Assert.Contains("2020", match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Kaguya_Season3_UsesYearAndEpisodes()
    {
        var match = SeasonMatcher.Match(Kaguya, new LibrarySeason(3, 2022, 4, 13));
        Assert.Equal(125367, Assert.Single(match!.Entries).Id);
    }

    [Fact]
    public void Kaguya_Season4_MatchesEvenWhenTheLibraryYearIsOneOff()
    {
        // TVDB dates the special by its theatrical release, AniList by its TV broadcast.
        var match = SeasonMatcher.Match(Kaguya, new LibrarySeason(4, 2022, 12, 4));
        Assert.Equal(151384, Assert.Single(match!.Entries).Id);
    }

    [Fact]
    public void Titan_Season3_MergesBothParts()
    {
        var match = SeasonMatcher.Match(Titan, new LibrarySeason(3, 2018, 7, 22));

        Assert.NotNull(match);
        Assert.Equal([99147, 104578], match!.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Titan_Season4_IsNotGivenPart2_WhenTheEpisodesDisagree()
    {
        // 28 episodes in the library: more than any single candidate aired. No guess is better than a wrong one.
        var match = SeasonMatcher.Match(Titan, new LibrarySeason(4, 2020, 12, 28));
        Assert.True(match == null || match.Entries.All(e => e.Id != 104578));
    }

    [Fact]
    public void PartialSeason_MatchesOnYearAndPosition()
    {
        // Only five of twelve episodes collected so far.
        var match = SeasonMatcher.Match(Kaguya, new LibrarySeason(2, 2020, null, 5));
        Assert.Equal(112641, Assert.Single(match!.Entries).Id);
    }

    [Fact]
    public void YearTwoApart_IsNeverAccepted()
    {
        Assert.Null(SeasonMatcher.Match(Kaguya, new LibrarySeason(2, 2024, null, 12)));
    }

    [Fact]
    public void SeasonOne_AndShortChains_HaveNoOwnMatch()
    {
        Assert.Null(SeasonMatcher.Match(Kaguya, new LibrarySeason(1, 2019, 1, 12)));
        Assert.Null(SeasonMatcher.Match(Kaguya.Take(1).ToArray(), new LibrarySeason(2, 2020, 4, 12)));
    }

    [Fact]
    public void PositionAlone_IsNotEnough()
    {
        // Nothing known about the season but its number.
        Assert.Null(SeasonMatcher.Match(Kaguya, new LibrarySeason(2, null, null, 0)));
    }

    [Fact]
    public void PickSequel_PrefersTvOverOnaAndMovies()
    {
        var media = new AniListMedia(1, "ANIME", "TV", 12, null, null, new AniListRelations(
        [
            new AniListEdge("SEQUEL", new AniListMedia(9, "ANIME", "MOVIE", 1, new AniListDate(2020, 1), null, null)),
            new AniListEdge("SEQUEL", new AniListMedia(8, "ANIME", "ONA", 6, new AniListDate(2019, 1), null, null)),
            new AniListEdge("SEQUEL", new AniListMedia(7, "ANIME", "TV", 12, new AniListDate(2021, 1), null, null)),
            new AniListEdge("PREQUEL", new AniListMedia(6, "ANIME", "TV", 12, new AniListDate(2015, 1), null, null)),
        ]));

        Assert.Equal(7, AniListClient.PickSequel(media));
    }
}
