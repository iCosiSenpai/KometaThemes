using System.Collections.ObjectModel;
using System.Text.Json;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Themes;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Theme selection and naming, on real animethemes.moe data.
/// </summary>
public class ThemeCatalogTests
{
    private static MediaTypeConfiguration All(bool skipOverlaps = true, bool creditlessOnly = false) => new()
    {
        FetchType = FetchType.All,
        IgnoreOverlapping = skipOverlaps,
        IgnoreThemesWithCredits = creditlessOnly,
        Volume = 0.5,
    };

    [Fact]
    public void Fixture_CarriesSongTitlesAndArtists()
    {
        var themes = ThemeCatalog.All([TestSupport.Fixture("kaguya-s1")]);

        Assert.Equal(["OP1", "ED1", "ED2"], themes.Select(t => t.Code));
        Assert.Equal("Love Dramatic feat. Rikka Ihara", themes[0].Title);
        Assert.Equal("Masayuki Suzuki", themes[0].Artists);
        Assert.Equal("Chikatto Chika Chika♡", themes[2].Title);
    }

    [Fact]
    public void SingleTheme_WithNoSequence_IsOp1_NotOp0()
    {
        // Kaguya-sama season 2 lists its only opening without a sequence; 1.x named it "OP0 - Op1".
        var opening = ThemeCatalog.All([TestSupport.Fixture("kaguya-s2")]).First(t => t.Theme.Type == ThemeType.OP);

        Assert.Null(opening.Theme.Sequence);
        Assert.Equal("OP1", opening.Code);
        Assert.Equal("OP1 - DADDY! DADDY! DO! feat. Airi Suzuki.mp3", ThemeNaming.FileName(opening.Code, opening.Title, ".mp3"));
    }

    [Fact]
    public void BestVersion_SkipsTheSpoilerEnding()
    {
        // Frieren's ED1 has three versions; the third is a spoiler transition.
        var ending = ThemeCatalog.All([TestSupport.Fixture("frieren")]).First(t => t.Code == "ED1");

        Assert.False(ending.Entry.Spoiler);
        Assert.True(ending.Video.Creditless);
    }

    [Fact]
    public void Single_TakesTheFirstOpening()
    {
        var settings = All();
        settings.FetchType = FetchType.Single;

        var chosen = ThemeCatalog.Select([TestSupport.Fixture("frieren")], settings, 5);

        Assert.Equal("OP1", Assert.Single(chosen).Code);
    }

    [Fact]
    public void Single_TakesTheFirstEnding_WhenOpeningsAreOff()
    {
        var settings = All();
        settings.FetchType = FetchType.Single;
        settings.IgnoreOPs = true;

        Assert.Equal("ED1", Assert.Single(ThemeCatalog.Select([TestSupport.Fixture("frieren")], settings, 5)).Code);
    }

    [Fact]
    public void Limit_And_Filters_Apply()
    {
        var anime = TestSupport.Anime(
            1,
            TestSupport.Theme(1, ThemeType.OP, 1, "Clean"),
            TestSupport.Theme(2, ThemeType.OP, 2, "Overlapping", overlap: OverlapType.Over),
            TestSupport.Theme(3, ThemeType.ED, 1, "Credits", creditless: false),
            TestSupport.Theme(4, ThemeType.ED, 2, "Second"));

        Assert.Equal(["Clean", "Credits", "Second"], ThemeCatalog.Select([anime], All(), 5).Select(c => c.Title));
        Assert.Equal(["Clean", "Second"], ThemeCatalog.Select([anime], All(creditlessOnly: true), 5).Select(c => c.Title));
        Assert.Equal(2, ThemeCatalog.Select([anime], All(), 2).Count);
    }

    [Fact]
    public void Disabled_SelectsNothing()
    {
        var settings = All();
        settings.FetchType = FetchType.None;
        Assert.Empty(ThemeCatalog.Select([TestSupport.Fixture("frieren")], settings, 5));
    }

    [Fact]
    public void UnknownThemeType_IsSkipped_NotFatal()
    {
        // A batch of a hundred anime used to fail on one unknown value.
        const string json = """
            {"id":1,"name":"X","slug":"x","year":2024,"animethemes":[
              {"id":5,"type":"IN","sequence":1,"slug":"IN1","animethemeentries":[]},
              {"id":6,"type":"OP","sequence":1,"slug":"OP1","animethemeentries":[{"id":7,"version":null,"episodes":"1-12","nsfw":false,"spoiler":false,
                "videos":[{"id":8,"basename":"X-OP1.webm","source":"HOLOGRAM","overlap":"Sideways","link":"https://v.animethemes.moe/X-OP1.webm","nc":true,"audio":{"id":9,"link":"https://a.animethemes.moe/X-OP1.ogg"}}]}]}],
             "resources":[]}
            """;
        var anime = JsonSerializer.Deserialize<Anime>(json)!;

        Assert.Equal(ThemeType.Other, anime.Themes![0].Type);
        var only = Assert.Single(ThemeCatalog.All([anime]));
        Assert.Equal(VideoSource.Unknown, only.Video.Source);
        Assert.Equal(OverlapType.None, only.Video.Overlap);
    }

    [Fact]
    public void SameSong_UnderAVariantCode_IsKeptOnce()
    {
        var dub = TestSupport.Theme(3, ThemeType.ED, 1, "Nandemonaiya") with { Slug = "ED1-EN" };
        var anime = TestSupport.Anime(
            9,
            TestSupport.Theme(1, ThemeType.OP, 1, "Yume Tourou"),
            dub,
            TestSupport.Theme(2, ThemeType.ED, 1, "Nandemonaiya"),
            TestSupport.Theme(4, ThemeType.ED, null, "bliss") with { Slug = "ED1-TV" });

        Assert.Equal(["OP1", "ED1", "ED1-TV"], ThemeCatalog.All([anime]).Select(c => c.Code));
    }

    [Theory]
    [InlineData("OP1", "Gurenge", ".mp3", "OP1 - Gurenge.mp3")]
    [InlineData("ED1-TV", "bliss", ".mp3", "ED1-TV - bliss.mp3")]
    [InlineData("OP2", null, ".webm", "OP2.webm")]
    [InlineData("OP1", "A/B: C*D?", ".mp3", "OP1 - A B C D.mp3")]
    [InlineData("OP1", "  ...  ", ".mp3", "OP1.mp3")]
    public void FileName_IsSafeAndReadable(string code, string? title, string extension, string expected)
        => Assert.Equal(expected, ThemeNaming.FileName(code, title, extension));

    [Fact]
    public void FileName_TruncatesLongTitles()
    {
        var name = ThemeNaming.FileName("OP1", new string('a', 300), ".mp3");
        Assert.True(name.Length <= ThemeNaming.MaxTitleLength + "OP1 - .mp3".Length);
    }

    [Theory]
    [InlineData("OP0 - Op1__50.mp3", 50)]
    [InlineData("ED2__0.webm", 0)]
    [InlineData("OP1 - Love Dramatic.mp3", null)]
    public void LegacyVolume_IsReadFrom1xNames(string name, int? expected)
        => Assert.Equal(expected, ThemeNaming.LegacyVolume(name));

    [Fact]
    public void Unique_AppendsANumber()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OP1 - X.mp3", "OP1 - X (2).mp3" };
        Assert.Equal("OP1 - X (3).mp3", ThemeNaming.Unique("OP1 - X.mp3", taken));
        Assert.Equal("op1 - x (3).mp3", ThemeNaming.Unique("op1 - x.mp3", taken));
    }

    [Theory]
    [InlineData("theme.mp3", true)]
    [InlineData("../theme.mp3", false)]
    [InlineData("a/b.mp3", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void IsPlainFileName_RejectsPaths(string name, bool expected)
        => Assert.Equal(expected, ThemeNaming.IsPlainFileName(name));

    [Fact]
    public void EpisodesOf_MergesVersions()
    {
        var ending = TestSupport.Fixture("frieren").Themes!.First(t => t.Slug == "ED1");
        Assert.Equal("1-14, 16, 17-27, 28", ThemeCatalog.EpisodesOf(ending));
    }

    [Fact]
    public void Collections_AreNeverNull_ForEmptyAnime()
    {
        var anime = new Anime(1, "Empty", "empty", null, null, null);
        Assert.Empty(ThemeCatalog.All([anime]));
        Assert.Empty(ThemeCatalog.Select([anime], All(), 5));
        Assert.Null(new Anime(2, "x", "x", null, new Collection<AnimeTheme>(), null).GetCover(true));
    }
}
