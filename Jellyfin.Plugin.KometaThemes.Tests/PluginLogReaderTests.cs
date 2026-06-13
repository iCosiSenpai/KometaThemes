using System;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Api;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class PluginLogReaderTests
{
    [Fact]
    public void ParsePluginEntries_KeepsOnlyPluginCategories()
    {
        var lines = new[]
        {
            "[2026-06-13 00:00:59.305 +02:00] [INF] [36] Jellyfin.Plugin.KometaThemes.LibrarySyncHandler: New Series detected",
            "[2026-06-13 00:01:00.000 +02:00] [INF] [12] Emby.Server.Implementations.IO.LibraryMonitor: foreign entry",
            "[2026-06-13 00:01:01.000 +02:00] [WRN] [36] Jellyfin.Plugin.KometaThemes.AnimeThemesDownloader: something odd"
        };

        var entries = PluginLogReader.ParsePluginEntries(lines, 200);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.StartsWith(PluginLogReader.CategoryPrefix, e.Category, StringComparison.Ordinal));
        Assert.Equal("INF", entries[0].Level);
        Assert.Equal("WRN", entries[1].Level);
        Assert.Equal("something odd", entries[1].Message);
    }

    [Fact]
    public void ParsePluginEntries_AppendsStackTraceToPluginEntry_Only()
    {
        var lines = new[]
        {
            "[2026-06-13 00:00:59.305 +02:00] [ERR] [36] Jellyfin.Plugin.KometaThemes.Sync.SyncThemesRunner: sync failed",
            "System.InvalidOperationException: boom",
            "   at Jellyfin.Plugin.KometaThemes.Sync.SyncThemesRunner.RunAsync()",
            "[2026-06-13 00:01:00.000 +02:00] [ERR] [12] jellyfin_ani_sync.SessionServerEntry: foreign error",
            "System.ArgumentNullException: not ours",
            "[2026-06-13 00:01:01.000 +02:00] [INF] [36] Jellyfin.Plugin.KometaThemes.LibrarySyncHandler: next entry"
        };

        var entries = PluginLogReader.ParsePluginEntries(lines, 200);

        Assert.Equal(2, entries.Count);
        Assert.Contains("System.InvalidOperationException: boom", entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("at Jellyfin.Plugin.KometaThemes.Sync.SyncThemesRunner.RunAsync()", entries[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not ours", entries[0].Message, StringComparison.Ordinal);
        Assert.Equal("next entry", entries[1].Message);
    }

    [Fact]
    public void ParsePluginEntries_MalformedTimestamp_KeepsRawString()
    {
        var lines = new[]
        {
            "[not-a-date] [INF] [1] Jellyfin.Plugin.KometaThemes.Plugin: custom template entry"
        };

        var entries = PluginLogReader.ParsePluginEntries(lines, 200);

        Assert.Single(entries);
        Assert.Null(entries[0].Timestamp);
        Assert.Equal("not-a-date", entries[0].RawTimestamp);
        Assert.Equal("custom template entry", entries[0].Message);
    }

    [Fact]
    public void ParsePluginEntries_MissingThreadId_StillParses()
    {
        var lines = new[]
        {
            "[2026-06-13 00:00:59.305 +02:00] [INF] Jellyfin.Plugin.KometaThemes.Plugin: no thread id template"
        };

        var entries = PluginLogReader.ParsePluginEntries(lines, 200);

        Assert.Single(entries);
        Assert.Equal("no thread id template", entries[0].Message);
    }

    [Fact]
    public void ParsePluginEntries_CapKeepsNewestEntries()
    {
        var lines = Enumerable.Range(0, 50).Select(i =>
            $"[2026-06-13 00:00:{i:00}.000 +02:00] [INF] [1] Jellyfin.Plugin.KometaThemes.Plugin: entry {i}");

        var entries = PluginLogReader.ParsePluginEntries(lines, 10);

        Assert.Equal(10, entries.Count);
        Assert.Equal("entry 40", entries[0].Message);
        Assert.Equal("entry 49", entries[^1].Message);
    }

    [Fact]
    public void ParsePluginEntries_ParsesTimestampWithOffset()
    {
        var lines = new[]
        {
            "[2026-06-13 00:00:59.305 +02:00] [INF] [1] Jellyfin.Plugin.KometaThemes.Plugin: ts check"
        };

        var entries = PluginLogReader.ParsePluginEntries(lines, 200);

        Assert.NotNull(entries[0].Timestamp);
        Assert.Equal(2026, entries[0].Timestamp!.Value.Year);
        Assert.Equal(TimeSpan.FromHours(2), entries[0].Timestamp!.Value.Offset);
    }

    [Fact]
    public void FindNewestLogFile_MissingDirectory_ReturnsNull()
    {
        Assert.Null(PluginLogReader.FindNewestLogFile("/nonexistent/dir/for/tests"));
        Assert.Null(PluginLogReader.FindNewestLogFile(string.Empty));
    }
}
