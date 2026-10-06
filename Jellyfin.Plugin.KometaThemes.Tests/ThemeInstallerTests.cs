using System.Diagnostics;
using System.Net;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// The installer end to end with a real ffmpeg: download, convert, rename, prune, clean up.
/// Skipped when ffmpeg is not installed.
/// </summary>
public sealed class ThemeInstallerTests : IDisposable
{
    private static readonly string? Ffmpeg = Find("ffmpeg");
    private static readonly string? Ffprobe = Find("ffprobe");

    private readonly TempFolder _temp = TestSupport.TempFolder();
    private readonly Guid _itemId = Guid.NewGuid();

    private static readonly Anime Show = TestSupport.Anime(
        500,
        TestSupport.Theme(1, ThemeType.OP, 1, "First"),
        TestSupport.Theme(2, ThemeType.ED, 1, "Ending"));

    public void Dispose() => _temp.Dispose();

    private static string? Find(string name)
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);

    private void Generate(string args)
    {
        using var process = Process.Start(new ProcessStartInfo(Ffmpeg!, "-nostdin -loglevel error -y " + args) { RedirectStandardError = true })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private (ThemeInstaller Installer, PluginConfiguration Config) Build()
    {
        var media = Path.Combine(_temp.Path, "source");
        Directory.CreateDirectory(media);
        var ogg = Path.Combine(media, "a.ogg");
        var webm = Path.Combine(media, "v.webm");
        if (!File.Exists(ogg))
        {
            Generate($"-f lavfi -i sine=frequency=440:duration=2 -c:a libopus \"{ogg}\"");
            Generate($"-f lavfi -i color=c=blue:s=64x36:d=2 -f lavfi -i sine=frequency=880:duration=2 -shortest -c:v libvpx-vp9 -c:a libopus \"{webm}\"");
        }

        var server = new StaticServer(ogg, webm);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ThemeInstaller.HttpClientName)).Returns(() => new HttpClient(server, disposeHandler: false));
        var encoder = new Mock<IMediaEncoder>();
        encoder.SetupGet(e => e.EncoderPath).Returns(Ffmpeg!);
        encoder.SetupGet(e => e.ProbePath).Returns(Ffprobe ?? string.Empty);

        var installer = new ThemeInstaller(
            factory.Object,
            encoder.Object,
            new TranscodeGate(),
            new FolderStateStore(NullLogger<FolderStateStore>.Instance),
            new FolderLocks(),
            NullLogger<ThemeInstaller>.Instance);
        var config = new PluginConfiguration();
        config.AudioSettings.FetchType = FetchType.All;
        config.AudioSettings.Volume = 0.5;
        config.VideoSettings.FetchType = FetchType.None;
        return (installer, config);
    }

    private string Show_ => Path.Combine(_temp.Path, "Show");

    private string MusicFile(string name) => Path.Combine(Show_, "theme-music", name);

    [Fact]
    public async Task Upgrade_RenamesDownloadsPrunesAndRemovesTheDuplicateCopy()
    {
        if (Ffmpeg == null)
        {
            return;
        }

        var (installer, config) = Build();
        var target = new ThemeFolder(_itemId, Show_, 0, true);

        // A 1.x folder: ending already downloaded under the old name, a stale theme, the owner's own
        // song, and the root theme.mp3 copy 1.x made of the ending.
        Directory.CreateDirectory(Path.Combine(Show_, "theme-music"));
        Generate($"-f lavfi -i sine=frequency=330:duration=1 -c:a libmp3lame \"{MusicFile("ED0 - Ed1__50.mp3")}\"");
        File.Copy(MusicFile("ED0 - Ed1__50.mp3"), Path.Combine(Show_, "theme.mp3"));
        File.WriteAllText(MusicFile("OP9 - Old__50.mp3"), "stale");
        File.WriteAllText(MusicFile("My own.mp3"), "mine");
        File.WriteAllText(
            Path.Combine(Show_, FolderStateStore.FileName),
            """[{"ThemeId":2,"Type":"ED","Sequence":0,"Slug":"ED1","FileName":"ED0 - Ed1__50.mp3"},{"ThemeId":99,"Type":"OP","Sequence":9,"Slug":"OP9","FileName":"OP9 - Old__50.mp3"}]""");

        var result = await installer.ApplyAsync(target, [Show], config, false, CancellationToken.None);

        Assert.Equal(0, result.Failed);
        Assert.Equal(1, result.Downloaded);
        Assert.Equal(1, result.Renamed);
        Assert.True(File.Exists(MusicFile("OP1 - First.mp3")));
        Assert.True(File.Exists(MusicFile("ED1 - Ending.mp3")));
        Assert.False(File.Exists(MusicFile("ED0 - Ed1__50.mp3")));
        Assert.False(File.Exists(MusicFile("OP9 - Old__50.mp3")));
        Assert.True(File.Exists(MusicFile("My own.mp3")));
        Assert.False(File.Exists(Path.Combine(Show_, "theme.mp3")));
        Assert.Empty(Directory.GetFiles(Show_, "*.kt-part", SearchOption.AllDirectories));

        var state = FolderStateStore.Parse(File.ReadAllText(Path.Combine(Show_, FolderStateStore.FileName)));
        Assert.Equal(FolderState.CurrentVersion, state.Version);
        Assert.Equal(["Ending", "First"], state.Records.Select(r => r.Title).Order());
        Assert.All(state.Records, r => Assert.Equal(50, r.EffectiveVolume));

        // Nothing left to do the second time.
        var again = await installer.ApplyAsync(target, [Show], config, false, CancellationToken.None);
        Assert.False(again.Changed);
    }

    [Fact]
    public async Task UserThemeMp3_IsNeverTouched()
    {
        if (Ffmpeg == null)
        {
            return;
        }

        var (installer, config) = Build();
        Directory.CreateDirectory(Show_);
        File.WriteAllText(Path.Combine(Show_, "theme.mp3"), "the owner's own theme");

        await installer.ApplyAsync(new ThemeFolder(_itemId, Show_, 0, true), [Show], config, false, CancellationToken.None);

        Assert.Equal("the owner's own theme", File.ReadAllText(Path.Combine(Show_, "theme.mp3")));
    }

    [Fact]
    public async Task Video_IsCopied_AndAnInheritingSeasonIsEmptied()
    {
        if (Ffmpeg == null || Ffprobe == null)
        {
            return;
        }

        var (installer, config) = Build();
        config.AudioSettings.FetchType = FetchType.None;
        config.VideoSettings.FetchType = FetchType.Single;
        config.VideoSettings.IgnoreThemesWithCredits = false;
        var season = new ThemeFolder(_itemId, Path.Combine(Show_, "Season 02"), 2, false);
        Directory.CreateDirectory(season.Folder);

        var first = await installer.ApplyAsync(season, [Show], config, false, CancellationToken.None);
        Assert.Equal(1, first.Downloaded);
        Assert.True(File.Exists(Path.Combine(season.Folder, "backdrops", "OP1 - First.webm")));

        // The season turns out to have no entry of its own: its files go, the folder is tidied.
        var cleared = await installer.ApplyAsync(season, [], config, false, CancellationToken.None);
        Assert.Equal(1, cleared.Deleted);
        Assert.False(Directory.Exists(Path.Combine(season.Folder, "backdrops")));
        Assert.False(File.Exists(Path.Combine(season.Folder, FolderStateStore.FileName)));
    }

    [Fact]
    public async Task DeletedByTheOwner_IsNotDownloadedAgain()
    {
        if (Ffmpeg == null)
        {
            return;
        }

        var (installer, config) = Build();
        var target = new ThemeFolder(_itemId, Show_, 0, true);
        Directory.CreateDirectory(Show_);
        await installer.ApplyAsync(target, [Show], config, false, CancellationToken.None);

        Assert.NotNull(await installer.DeleteRecordedFileAsync(Show_, "theme-music", "ED1 - Ending.mp3", CancellationToken.None));
        Assert.Null(await installer.DeleteRecordedFileAsync(Show_, "theme-music", "../escape.mp3", CancellationToken.None));

        var after = await installer.ApplyAsync(target, [Show], config, false, CancellationToken.None);
        Assert.Equal(0, after.Downloaded);
        Assert.False(File.Exists(MusicFile("ED1 - Ending.mp3")));
    }

    [Fact]
    public async Task FailedDownload_LeavesNoTrace()
    {
        if (Ffmpeg == null)
        {
            return;
        }

        var (installer, config) = Build();
        var video = new Video(70, "Broken-OP1.webm", VideoSource.BD, OverlapType.None, "https://v.animethemes.moe/Broken-OP1.webm", true, new Audio(71, "https://a.animethemes.moe/Broken-OP1.ogg"));
        var entry = new AnimeThemeEntry(72, 1, "1-12", false, false, [video]);
        var broken = TestSupport.Anime(600, new AnimeTheme(7, ThemeType.OP, 1, "OP1", new Song(7, "Broken", null), [entry]));
        Directory.CreateDirectory(Show_);

        var result = await installer.ApplyAsync(new ThemeFolder(_itemId, Show_, 0, true), [broken], config, false, CancellationToken.None);

        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.False(Directory.Exists(Path.Combine(Show_, "theme-music")) && Directory.EnumerateFiles(Path.Combine(Show_, "theme-music")).Any());
    }

    /// <summary>
    /// Serves the generated files for any animethemes-like link; links containing 404 fail.
    /// </summary>
    private sealed class StaticServer : HttpMessageHandler
    {
        private readonly string _audio;
        private readonly string _video;

        public StaticServer(string audio, string video)
        {
            _audio = audio;
            _video = video;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("Broken", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var file = path.EndsWith(".ogg", StringComparison.Ordinal) ? _audio : _video;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) });
        }
    }
}
