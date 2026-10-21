using System.Text.Json;
using Jellyfin.Plugin.KometaThemes.Resolving;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Season matching against real data: the seasons of the owner's library as Jellyfin dates and counts
/// them, and each series' franchise as animethemes.moe groups it (year, season, format, and the last
/// episode a theme is listed for). Saved on 2026-10-21, while AniList's API was switched off.
/// </summary>
public class SeasonMatcherTests
{
    private static readonly Dictionary<string, (int Root, LibrarySeason[] Seasons, FranchiseEntry[] Entries)> Library = Load();

    public static TheoryData<string, int, int[]> Expected => new()
    {
        { "3D Kanojo: Real Girl", 2, [20] },
        { "The Angel Next Door Spoils Me Rotten", 2, [4791] },
        { "Arrivare a te", 2, [1508] },
        { "Arrivare a te", 3, [4386] },
        { "The Asterisk War", 2, [837] },
        { "Black Clover", 2, [4963] },
        { "The Café Terrace and Its Goddesses", 2, [4342] },
        { "A Certain Magical Index", 2, [2958] },
        { "A Certain Magical Index", 3, [2959] },
        { "Clannad", 2, [487] },
        { "Da campagnolo stagionato a gran maestro di spada", 2, [4916] },
        { "The Dangers in My Heart", 2, [4212] },
        { "DanMachi", 2, [681] },
        { "DanMachi", 3, [683] },
        { "DanMachi", 4, [3866, 3957] },
        { "DanMachi", 5, [4403] },
        { "The Faraway Paladin", 2, [4171] },
        { "Fruits Basket 1st Season", 2, [785] },
        { "Fruits Basket 1st Season", 3, [3324] },
        { "Grand Blue Dreaming", 2, [4610] },
        { "Grand Blue Dreaming", 3, [4902] },
        { "HELL MODE - Il giocatore hardcore domina in un altro mondo con settaggi folli", 2, [4873] },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void RealLibrary_LaterSeasonsGetTheirOwnEntry(string series, int season, int[] entries)
    {
        var match = Match(series, season);
        Assert.Equal(entries, match.EntryIds);
        Assert.Null(match.Window);
    }

    [Theory]
    [InlineData("3D Kanojo: Real Girl")]
    [InlineData("A Certain Magical Index")]
    [InlineData("DanMachi")]
    [InlineData("Fruits Basket 1st Season")]
    public void RealLibrary_TheFirstSeasonIsTheSeriesOwnEntry(string series)
        => Assert.Equal([Library[series].Root], Match(series, 1).EntryIds);

    [Fact]
    public void DanMachi_TheSpinOffIsNotTheSecondSeason()
    {
        // No IDs, no premiere dates: Sword Oratoria (2017, TV, 12 episodes) sits between the first and
        // second season and fits the second season's episode count as well as II does.
        Assert.DoesNotContain(684, Match("DanMachi", 2).EntryIds);
    }

    [Theory]
    [InlineData("Ascendance of a Bookworm", 4)] // no premiere date, and four candidates of about its length
    [InlineData("The Disastrous Life of Saiki K.", 3)] // its entry is not on animethemes.moe
    public void RealLibrary_UncertainSeasonsPlayTheSeriesThemes(string series, int season)
    {
        var match = Match(series, season);
        Assert.False(match.Matched, $"matched {string.Join(",", match.EntryIds)}: {match.Reason}");
        Assert.StartsWith("plays the series themes", match.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LongEntry_SplitAcrossSeasons_GivesEachSeasonItsEpisodes()
    {
        // TVDB splits Black Clover's 170 episodes into four seasons; animethemes.moe has one entry.
        FranchiseEntry[] entries =
        [
            new(308, "Black Clover (TV)", 2017, 10, "TV", 170, false, false),
            new(4963, "Black Clover 2nd Season", 2026, 10, "TV", 1, false, false),
        ];
        LibrarySeason[] seasons = [new(1, 2017, 10, 51), new(2, 2018, 10, 51), new(3, 2019, 10, 52), new(4, 2020, 10, 16), new(5, 2026, 10, 3)];

        var matches = SeasonMatcher.Align(seasons, entries, 308);

        Assert.Equal([308], matches[0].EntryIds);
        Assert.Equal(new EpisodeWindow(52, 102), matches[1].Window);
        Assert.Equal(new EpisodeWindow(103, 154), matches[2].Window);
        Assert.Equal(new EpisodeWindow(155, 170), matches[3].Window);
        Assert.Equal([4963], matches[4].EntryIds);
        Assert.Null(matches[4].Window);
    }

    [Fact]
    public void SwordArtOnline_MergesBothPartsAcrossTheRecap()
    {
        // animethemes.moe on 2026-10-21: the "Reflection" recap premiered between the two parts of War of
        // Underworld, a movie between II and Alicization, the Alternative spin-off in between too. Episode
        // counts are the last episode the themes list.
        FranchiseEntry[] entries =
        [
            new(2801, "Sword Art Online", 2012, 7, "TV", 25, false, false),
            new(2806, "Sword Art Online II", 2014, 7, "TV", 24, false, false),
            new(2808, "Sword Art Online Movie: Ordinal Scale", 2017, 1, "Movie", null, false, false),
            new(2807, "Sword Art Online Alternative: Gun Gale Online", 2018, 4, "TV", 12, false, false),
            new(2802, "Sword Art Online: Alicization", 2018, 10, "TV", 24, false, false),
            new(2803, "Sword Art Online: Alicization - War of Underworld", 2019, 10, "TV", 12, false, false),
            new(2805, "Sword Art Online: Alicization - War of Underworld Reflection", 2019, 10, "TV", null, false, false),
            new(2804, "Sword Art Online: Alicization - War of Underworld 2nd Season", 2020, 7, "TV", 12, false, false),
            new(3854, "Sword Art Online: Progressive Movie - Hoshi Naki Yoru no Aria", 2021, 10, "Movie", null, false, false),
            new(4410, "Sword Art Online Alternative: Gun Gale Online II", 2024, 10, "TV", 12, false, false),
        ];
        LibrarySeason[] seasons = [new(1, 2012, 7, 25), new(2, 2014, 7, 24), new(3, 2018, 10, 24), new(4, 2019, 10, 23)];

        var matches = SeasonMatcher.Align(seasons, entries, 2801);

        Assert.Equal([2806], matches[1].EntryIds);
        Assert.Equal([2802], matches[2].EntryIds);
        Assert.Equal([2803, 2804], matches[3].EntryIds);
    }

    [Fact]
    public void MovieBetweenSeasons_DoesNotStopTheMatch()
    {
        // KonoSuba: the Crimson Legend movie premiered between the second and third season.
        FranchiseEntry[] entries =
        [
            new(1590, "Kono Subarashii Sekai ni Shukufuku wo!", 2016, 1, "TV", 10, false, false),
            new(1591, "Kono Subarashii Sekai ni Shukufuku wo! 2", 2017, 1, "TV", 10, false, false),
            new(1592, "Kono Subarashii Sekai ni Shukufuku wo! Movie: Kurenai Densetsu", 2019, 7, "Movie", null, false, false),
            new(4171, "Kono Subarashii Sekai ni Shukufuku wo! 3", 2024, 4, "TV", 11, false, false),
        ];
        LibrarySeason[] seasons = [new(1, 2016, 1, 10), new(2, 2017, 1, 10), new(3, 2024, 4, 11)];

        var matches = SeasonMatcher.Align(seasons, entries, 1590);

        Assert.Equal([1591], matches[1].EntryIds);
        Assert.Equal([4171], matches[2].EntryIds);
    }

    [Fact]
    public void ExactEpisodeCounts_RuleOutAShorterEntry()
    {
        // AniList's count is the aired count: a season with more episodes than an entry is another one.
        FranchiseEntry[] entries =
        [
            new(1, "Show", 2020, 1, "TV", 12, true, true),
            new(2, "Show 2", 2021, 1, "TV", 12, true, true),
        ];
        var match = SeasonMatcher.Align([new(1, 2020, 1, 12), new(2, 2021, 1, 24)], entries, 1)[1];
        Assert.False(match.Matched);
    }

    [Fact]
    public void FarFromThePremiere_IsNeverMatched()
    {
        FranchiseEntry[] entries =
        [
            new(1, "Show", 2010, 4, "TV", 12, false, false),
            new(2, "Show 2nd Season", 2012, 4, "TV", 12, false, false),
        ];

        // Same name and episode count, but the library season premiered three years later.
        var match = SeasonMatcher.Align([new(1, 2010, 4, 12), new(2, 2015, 4, 12)], entries, 1)[1];
        Assert.False(match.Matched);
    }

    private static SeasonMatch Match(string series, int season)
    {
        var (root, seasons, entries) = Library[series];
        return SeasonMatcher.Align(seasons, entries, root).Single(m => m.SeasonNumber == season);
    }

    private static Dictionary<string, (int, LibrarySeason[], FranchiseEntry[])> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "franchises.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, (int, LibrarySeason[], FranchiseEntry[])>();
        foreach (var series in document.RootElement.EnumerateArray())
        {
            var seasons = series.GetProperty("seasons").EnumerateArray()
                .Select(s => new LibrarySeason(s.GetProperty("number").GetInt32(), Int(s, "year"), Int(s, "month"), s.GetProperty("eps").GetInt32()))
                .ToArray();
            var entries = series.GetProperty("entries").EnumerateArray()
                .Select(e => new FranchiseEntry(e.GetProperty("id").GetInt32(), e.GetProperty("name").GetString()!, Int(e, "year"), Int(e, "month"), e.GetProperty("format").GetString(), Int(e, "eps"), false, false))
                .ToArray();
            result[series.GetProperty("name").GetString()!] = (series.GetProperty("root").GetInt32(), seasons, entries);
        }

        return result;
    }

    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
