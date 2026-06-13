using System;
using System.IO;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class JsonResolutionCacheTests
{
    [Fact]
    public void Constructor_Loads_Empty_WhenFile_Missing()
    {
        var paths = new MockApplicationPaths();
        var cache = new JsonResolutionCache(paths, NullLogger<JsonResolutionCache>.Instance);
        var stats = cache.GetStats();
        Assert.Equal(0, stats.PositiveEntries);
        Assert.Equal(0, stats.NegativeEntries);
        cache.Dispose();
    }

    [Fact]
    public void SetPositive_ThenTryGet_ReturnsValue()
    {
        var paths = new MockApplicationPaths();
        var cache = new JsonResolutionCache(paths, NullLogger<JsonResolutionCache>.Instance);
        var anime = new Anime(42, "Test", "test", null, new System.Collections.ObjectModel.Collection<AnimeTheme>(), null);
        cache.SetPositive("k1", new[] { anime });

        Assert.True(cache.TryGet("k1", out var result));
        Assert.NotNull(result);
        Assert.Equal(42, result![0].Id);
        cache.Dispose();
    }

    [Fact]
    public void SetNegative_ThenTryGet_ReturnsNull_ButHits()
    {
        var paths = new MockApplicationPaths();
        var cache = new JsonResolutionCache(paths, NullLogger<JsonResolutionCache>.Instance);
        cache.SetNegative("k1");

        Assert.True(cache.TryGet("k1", out var result));
        Assert.Null(result);
        Assert.True(cache.GetStats().TotalHits > 0);
        cache.Dispose();
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var paths = new MockApplicationPaths();
        var cache = new JsonResolutionCache(paths, NullLogger<JsonResolutionCache>.Instance);
        cache.SetPositive("k1", new[] { new Anime(1, "t", "t", null, new System.Collections.ObjectModel.Collection<AnimeTheme>(), null) });
        cache.SetNegative("k2");
        cache.Clear();

        Assert.False(cache.TryGet("k1", out _));
        Assert.False(cache.TryGet("k2", out _));
        cache.Dispose();
    }

    private sealed class MockApplicationPaths : MediaBrowser.Common.Configuration.IApplicationPaths
    {
        public MockApplicationPaths()
        {
            PluginConfigurationsPath = Path.Combine(Path.GetTempPath(), "kometathemes_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(PluginConfigurationsPath);
        }

        public string PluginConfigurationsPath { get; }

        public string DataPath => Path.GetTempPath();
        public string ProgramDataPath => Path.GetTempPath();
        public string WebPath => Path.GetTempPath();
        public string ProgramSystemPath => Path.GetTempPath();
        public string AppLocalizationPath => Path.GetTempPath();
        public string ImageCachePath => Path.GetTempPath();
        public string MetadataPath => Path.GetTempPath();
        public string CachePath => Path.GetTempPath();
        public string LogConfigurationPath => Path.GetTempPath();
        public string InternalMetadataPath => Path.GetTempPath();
        public string TranscriptionPath => Path.GetTempPath();
        public string RecordingsPath => Path.GetTempPath();
        public string VirtualDataPath => Path.GetTempPath();
        public string PluginSystemConfigurationsPath => PluginConfigurationsPath;
        public string TempPath => Path.GetTempPath();
        public string ItemRepositoryPath => Path.GetTempPath();
        public string BackupPath => Path.GetTempPath();
        public string ConfigurationDirectoryPath => Path.GetTempPath();
        public string LogDirectoryPath => Path.GetTempPath();
        public string SystemConfigurationFilePath => Path.GetTempPath();
        public string PluginsPath => Path.GetTempPath();
        public string TempDirectory => Path.GetTempPath();
        public string TrickplayPath => Path.GetTempPath();
        public void MakeSanityCheckOrThrow() { }
        public void CreateAndCheckMarker(string directory, string markerName, bool recursive) { }
    }
}
