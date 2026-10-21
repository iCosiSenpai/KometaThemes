using System.Text.Json;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Title matching against real animethemes.moe search results, for anime 2.0 could not find.
/// </summary>
public class TitleMatcherTests
{
    private const int Required = 80;

    private static IReadOnlyList<Anime> Search(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "search-" + name + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("search").GetProperty("anime").Deserialize<List<Anime>>()!;
    }

    private static TitleQuery Series(int? year, params string[] titles) => new(titles, [], year is { } y ? [y] : [], null, false);

    private static (Anime Anime, int Score) Best(TitleQuery query, string fixture) => TitleMatcher.Rank(query, Search(fixture))[0];

    [Fact]
    public void DanMachi_IsFoundByItsAbbreviation()
    {
        // The library calls it "DanMachi"; animethemes.moe lists that as a synonym of the long title.
        var best = Best(Series(2015, "DanMachi"), "danmachi");

        Assert.Equal(680, best.Anime.Id);
        Assert.True(best.Score >= Required, $"score {best.Score}");
    }

    [Fact]
    public void DanMachi_LaterSeasonsRankBelowTheFirst()
    {
        var ranked = TitleMatcher.Rank(Series(2015, "DanMachi", "Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka"), Search("danmachi"));

        Assert.Equal(680, ranked[0].Anime.Id);
        Assert.All(ranked.Skip(1), other => Assert.True(other.Score < ranked[0].Score, $"{other.Anime.Name} scored {other.Score}"));
    }

    [Fact]
    public void Takagi_MatchesDespiteAnotherRomanization()
    {
        // "Jozu" in the library, "Jouzu" on animethemes.moe; Jellyfin's AniList ID points at an OVA
        // that animethemes.moe does not have, so the title is the only way in.
        var query = Series(2018, "Karakai Jozu no Takagi-san", "Karakai Jouzu no Takagi-san: Water Slide");
        var ranked = TitleMatcher.Rank(query, Search("takagi"));

        Assert.Equal(1448, ranked[0].Anime.Id);
        Assert.True(ranked[0].Score >= Required);
        Assert.True(ranked[1].Score < Required, "the second and third seasons must not pass as the series");
    }

    [Fact]
    public void CafeTerrace_IsFoundByItsEnglishTitle()
    {
        var best = Best(Series(2023, "The Café Terrace and Its Goddesses", "女神のカフェテラス"), "cafe-terrace");

        Assert.Equal(4051, best.Anime.Id);
        Assert.True(best.Score >= Required);
    }

    [Fact]
    public void Bookworm_PrefersTheFirstSeason_WhenLaterOnesShareTheTitle()
    {
        // "Ascendance of a Bookworm" is a synonym of both the first and the second season.
        var best = Best(Series(2019, "Ascendance of a Bookworm", "Honzuki no Gekokujou: Shisho ni Naru Tame ni wa Shudan o Erande Iraremasen"), "bookworm");

        Assert.Equal(1182, best.Anime.Id);
        Assert.True(best.Score >= Required);
    }

    [Fact]
    public void UmaMusume_ExactTitleBeatsSequelsAndSpinOffs()
    {
        var best = Best(Series(2018, "Uma Musume: Pretty Derby"), "uma-musume");
        Assert.Equal(3060, best.Anime.Id);
    }

    [Fact]
    public void DevilMayCry2025_IsNotThe2007Series()
    {
        // The year in the title tells the remake from the original: same name, different show.
        var query = new TitleQuery(["Devil May Cry"], [], [2025], 2025, false);
        Assert.True(Best(query, "devil-may-cry").Score < Required);
    }

    [Fact]
    public void Paprika_IsFoundFromTheOriginalPartOfALocalizedTitle()
    {
        var query = TitleQueryFor(movie: true, "Paprika - Sognando un sogno", folder: "Paprika.2006.4K.HDR.DV.2160p.BDRip Ita Eng Jap x265-NAHOM");
        var best = Best(query, "paprika");

        Assert.Equal(2227, best.Anime.Id);
        Assert.True(best.Score >= Required, $"score {best.Score}");
    }

    [Fact]
    public void TakagiMovie_EigaMeansMovie()
    {
        var query = TitleQueryFor(movie: true, "Eiga Karakai Jouzu no Takagi-san", folder: "Karakai Jouzu no Takagi-san Movie");
        var movie = TitleMatcher.Score(query, Search("takagi-movie")[0]);
        var series = TitleMatcher.Score(query, Search("takagi")[0]);

        Assert.True(movie >= Required, $"movie {movie}");
        Assert.True(series < movie, "the TV series is not the movie");
    }

    [Fact]
    public void Camelot2_IsNotMatchedToCamelot1()
    {
        // Jellyfin names the movie without its number; the folder says it is the second one, which
        // animethemes.moe does not have. Only the first comes back from the search.
        var query = TitleQueryFor(movie: true, "Fate/Grand Order: Shinsei Entaku Ryouiki Camelot", folder: "Fate+Grand Order the Movie - Divine Realm of the Round Table - Camelot 2 Paladin; Agateram (2021)");
        Assert.True(Best(query, "camelot").Score < Required);
    }

    [Fact]
    public void AMovieIsNotMatchedToATvSeries()
    {
        var query = new TitleQuery(["Uma Musume: Pretty Derby"], [], [2018], null, true);
        var tv = Search("uma-musume").Single(a => a.Id == 3060);
        Assert.True(TitleMatcher.Score(query, tv) < Required);
    }

    [Theory]
    [InlineData("Kaguya-sama wa Kokurasetai?: Tensai-tachi no Renai Zunousen", null)]
    [InlineData("Karakai Jouzu no Takagi-san 3", 3)]
    [InlineData("Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka IV: Shin Shou - Meikyuu-hen", 4)]
    [InlineData("Uma Musume: Pretty Derby Season 3", 3)]
    [InlineData("Megami no Café Terrace 2nd Season", 2)]
    [InlineData("Sword Art Online II", 2)]
    [InlineData("Shingeki no Kyojin Season 3 Part 2", 3)]
    [InlineData("Fruits Basket 1st Season", 1)]
    [InlineData("Fate/Zero Second Season", 2)]
    [InlineData("86 Eighty Six", null)]
    [InlineData("Mob Psycho 100", null)]
    [InlineData("Steins;Gate 0", null)]
    public void SeasonOrdinal_ReadsTheUsualSpellings(string title, int? expected)
        => Assert.Equal(expected, TitleMatcher.SeasonOrdinal(title));

    [Theory]
    [InlineData("DanMachi (2015) [au-9fa88c3f5dee]", "DanMachi", 2015)]
    [InlineData("Paprika.2006.4K.HDR.DV.2160p.BDRip Ita Eng Jap x265-NAHOM", "Paprika", 2006)]
    [InlineData("Saekano the Movie Finale (2019) {tmdb-608826} - [Bluray-1080p][FLAC 3.0][JA][x265 10bit]-Kawatare", "Saekano the Movie Finale", 2019)]
    [InlineData("Devil May Cry (2025)", "Devil May Cry", 2025)]
    [InlineData("86 Eighty Six", "86 Eighty Six", null)]
    [InlineData("Fate+Grand Order the Movie - Divine Realm of the Round Table - Camelot 2 Paladin; Agateram (2021)", "Fate/Grand Order the Movie - Divine Realm of the Round Table - Camelot 2 Paladin; Agateram", 2021)]
    public void Clean_KeepsTheTitleAndTheYearOfAFolderName(string raw, string title, int? year)
        => Assert.Equal((title, year), TitleSearchResolver.Clean(raw));

    private static TitleQuery TitleQueryFor(bool movie, string name, string folder)
    {
        var (folderTitle, folderYear) = TitleSearchResolver.Clean(folder);
        var titles = new List<string> { name, folderTitle };
        var weak = titles.SelectMany(t => t.Split(" - ")).Where(p => !titles.Contains(p)).ToList();
        return new TitleQuery(titles, weak, folderYear is { } y ? [y] : [], null, movie);
    }
}
