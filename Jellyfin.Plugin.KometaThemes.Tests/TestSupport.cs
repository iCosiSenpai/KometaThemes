using System.Collections.ObjectModel;
using System.Text.Json;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Common.Configuration;
using Moq;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Shared helpers: real fixtures, temporary folders and application paths.
/// </summary>
internal static class TestSupport
{
    /// <summary>
    /// Loads an anime saved from the live animethemes.moe API.
    /// </summary>
    public static Anime Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("anime").Deserialize<Anime>()!;
    }

    /// <summary>
    /// Creates a temporary folder removed by the returned handle.
    /// </summary>
    public static TempFolder TempFolder() => new();

    /// <summary>
    /// Application paths whose plugin configuration folder is a temporary folder.
    /// </summary>
    public static IApplicationPaths Paths(string root)
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(root);
        paths.SetupGet(p => p.CachePath).Returns(root);
        paths.SetupGet(p => p.PluginsPath).Returns(root);
        paths.SetupGet(p => p.TempDirectory).Returns(root);
        return paths.Object;
    }

    /// <summary>
    /// Builds a small anime by hand, for rules the fixtures do not cover.
    /// </summary>
    public static Anime Anime(int id, params AnimeTheme[] themes)
        => new(id, "Anime " + id, "anime_" + id, 2020, new Collection<AnimeTheme>(themes), null);

    /// <summary>
    /// Builds a theme with one entry and one video.
    /// </summary>
    public static AnimeTheme Theme(
        int id,
        ThemeType type,
        int? sequence,
        string? title,
        bool creditless = true,
        OverlapType overlap = OverlapType.None,
        bool spoiler = false,
        string? episodes = null)
    {
        var video = new Video(id * 10, $"Show-{type}{sequence}.webm", VideoSource.BD, overlap, $"https://v.animethemes.moe/Show-{type}{sequence}.webm", creditless, new Audio(id * 100, $"https://a.animethemes.moe/Show-{type}{sequence}.ogg"))
        {
            Resolution = 1080,
        };
        var entry = new AnimeThemeEntry(id * 1000, 1, episodes, false, spoiler, new Collection<Video> { video });
        var song = title == null ? null : new Song(id, title, new Collection<Artist> { new(1, "Artist", "artist") });
        return new AnimeTheme(id, type, sequence, $"{type}{sequence}", song, new Collection<AnimeThemeEntry> { entry });
    }
}

/// <summary>
/// A temporary folder deleted on dispose.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kt-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string relative, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
