using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Themes;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The plan that turns a folder's files into the wanted ones.
/// </summary>
public class ThemePlannerTests
{
    private static readonly Anime Show = TestSupport.Anime(
        77,
        TestSupport.Theme(1, ThemeType.OP, 1, "First"),
        TestSupport.Theme(2, ThemeType.ED, 1, "Ending"));

    private static List<ThemeChoice> Choices => ThemeCatalog.All([Show]).ToList();

    private static DesiredFile Want(int themeId, MediaType media, string name, int volume = 50, bool kept = false)
        => new(Choices.Single(c => c.Theme.Id == themeId), media, name, volume, kept);

    private static ThemeRecord Record(int themeId, string name, string directory = "theme-music", ThemeSource source = ThemeSource.AnimeThemes, int? volume = null)
        => new() { ThemeId = themeId, FileName = name, Directory = directory, Source = source, Volume = volume };

    [Fact]
    public void Legacy1xFile_IsRenamed_NotDownloadedAgain()
    {
        var records = new List<ThemeRecord> { Record(1, "OP0 - Op1__50.mp3") };
        var plan = ThemePlanner.Build(records, [Want(1, MediaType.Audio, "OP1 - First.mp3")], (_, _) => true);

        Assert.Empty(plan.Download);
        Assert.Equal("OP1 - First.mp3", Assert.Single(plan.Rename).Desired.FileName);
    }

    [Fact]
    public void VolumeChange_DownloadsAgain_AndReplaces()
    {
        var old = Record(1, "OP1 - First.mp3", volume: 30);
        var plan = ThemePlanner.Build([old], [Want(1, MediaType.Audio, "OP1 - First.mp3", volume: 50)], (_, _) => true);

        var download = Assert.Single(plan.Download);
        Assert.Same(old, download.Replaces);
    }

    [Fact]
    public void UnwantedThemeFile_IsDeleted_ButImportsAndUnknownFilesAreNot()
    {
        var auto = Record(2, "ED1 - Ending.mp3");
        var imported = Record(-5, "OP9 - From YouTube.mp3", source: ThemeSource.YouTube);
        var plan = ThemePlanner.Build([auto, imported], [Want(1, MediaType.Audio, "OP1 - First.mp3")], (_, _) => true);

        Assert.Equal(auto, Assert.Single(plan.Delete));
        Assert.Single(plan.Download);
    }

    [Fact]
    public void MissingFile_IsForgotten_AndFetchedAgain()
    {
        var gone = Record(1, "OP1 - First.mp3");
        var plan = ThemePlanner.Build([gone], [Want(1, MediaType.Audio, "OP1 - First.mp3")], (_, _) => false);

        Assert.Same(gone, Assert.Single(plan.Forget));
        Assert.Null(Assert.Single(plan.Download).Replaces);
    }

    [Fact]
    public void SameTheme_AsSongAndVideo_AreSeparateFiles()
    {
        var song = Record(1, "OP1 - First.mp3");
        var video = Record(1, "OP1 - First.webm", "backdrops");
        var plan = ThemePlanner.Build(
            [song, video],
            [Want(1, MediaType.Audio, "OP1 - First.mp3"), Want(1, MediaType.Video, "OP1 - First.webm")],
            (_, _) => true);

        Assert.True(plan.IsEmpty);
        Assert.Equal(2, plan.Keep.Count);
    }

    [Fact]
    public void Desired_AppliesOverrides_AndAvoidsForeignNames()
    {
        var state = new FolderState();
        state.SetOverride(2, MediaType.Audio, true);  // keep the ending although the settings skip it
        state.SetOverride(1, MediaType.Video, false); // never the opening video
        var opening = Choices.Where(c => c.Theme.Id == 1).ToList();
        var foreign = new Dictionary<string, HashSet<string>>
        {
            ["theme-music"] = new(StringComparer.OrdinalIgnoreCase) { "OP1 - First.mp3" },
        };

        var desired = ThemePlanner.Desired(opening, opening, Choices, state, 50, 40, foreign);

        Assert.Equal(["OP1 - First (2).mp3", "ED1 - Ending.mp3"], desired.Where(d => d.Media == MediaType.Audio).Select(d => d.FileName));
        Assert.True(desired.Single(d => d.Choice.Theme.Id == 2).Kept);
        Assert.DoesNotContain(desired, d => d.Media == MediaType.Video);
    }

    [Fact]
    public void FolderState_Reads1xArray_AndWrites2()
    {
        const string legacy = """
            [{"ThemeId":13053,"Type":"ED","Sequence":0,"Slug":"ED1","FileName":"ED0 - Ed1__50.mp3","SeasonNumber":0,"DownloadedAt":"2026-07-13T22:09:01Z","ItemId":"26a14c52-da11-4ff2-c790-71961042ea97"}]
            """;

        var state = FolderStateStore.Parse(legacy);
        var record = Assert.Single(state.Records);

        Assert.Equal(1, state.Version);
        Assert.Equal(ThemeType.ED, record.Type);
        Assert.Equal("theme-music", record.EffectiveDirectory);
        Assert.Equal(50, record.EffectiveVolume);
        Assert.Equal(ThemeSource.AnimeThemes, record.Source);

        var again = FolderStateStore.Parse(System.Text.Json.JsonSerializer.Serialize(state));
        Assert.Equal("ED0 - Ed1__50.mp3", Assert.Single(again.Records).FileName);
    }

    [Fact]
    public async Task FolderStateStore_MovesADamagedFileAside()
    {
        using var temp = TestSupport.TempFolder();
        temp.File(FolderStateStore.FileName, "[{\"ThemeId\":");
        var store = new FolderStateStore(Microsoft.Extensions.Logging.Abstractions.NullLogger<FolderStateStore>.Instance);

        var state = await store.LoadAsync(temp.Path, CancellationToken.None);

        Assert.Empty(state.Records);
        Assert.True(File.Exists(Path.Combine(temp.Path, FolderStateStore.FileName + ".corrupt")));
    }
}
