using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class FailedItemsStoreTests
{
    [Fact]
    public void Record_NewItem_AppearsInList()
    {
        var paths = new MockApplicationPaths();
        using var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        var item = MakeSeries("Test Anime", 2024);

        store.Record(item, FailedItemReason.Unresolved, null);

        var all = store.GetAll();
        Assert.Single(all);
        Assert.Equal("Test Anime", all[0].Name);
        Assert.Equal(FailedItemReason.Unresolved, all[0].Reason);
        Assert.Equal(1, all[0].Attempts);
        Assert.Equal(2024, all[0].ProductionYear);
    }

    [Fact]
    public void Record_SameItemTwice_BumpsAttempts_AndUpdatesReason()
    {
        var paths = new MockApplicationPaths();
        using var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        var item = MakeSeries("Test Anime", 2024);

        store.Record(item, FailedItemReason.Unresolved, null);
        store.Record(item, FailedItemReason.DownloadFailed, "timeout");

        var all = store.GetAll();
        Assert.Single(all);
        Assert.Equal(2, all[0].Attempts);
        Assert.Equal(FailedItemReason.DownloadFailed, all[0].Reason);
        Assert.Equal("timeout", all[0].Error);
    }

    [Fact]
    public void Remove_TrackedItem_ReturnsTrue_AndShrinksList()
    {
        var paths = new MockApplicationPaths();
        using var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        var item = MakeSeries("Test Anime", null);
        store.Record(item, FailedItemReason.Unresolved, null);

        Assert.True(store.Remove(item.Id));
        Assert.False(store.Remove(item.Id));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void RemoveIfUnresolved_LeavesDownloadFailures()
    {
        var paths = new MockApplicationPaths();
        using var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        var unresolved = MakeSeries("Unresolved Anime", null);
        var failed = MakeSeries("Failed Anime", null);
        store.Record(unresolved, FailedItemReason.Unresolved, null);
        store.Record(failed, FailedItemReason.DownloadFailed, "boom");

        store.RemoveIfUnresolved(unresolved.Id);
        store.RemoveIfUnresolved(failed.Id);

        var all = store.GetAll();
        Assert.Single(all);
        Assert.Equal("Failed Anime", all[0].Name);
    }

    [Fact]
    public void Clear_RemovesEverything_AndReturnsCount()
    {
        var paths = new MockApplicationPaths();
        using var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        store.Record(MakeSeries("A", null), FailedItemReason.Unresolved, null);
        store.Record(MakeSeries("B", null), FailedItemReason.DownloadFailed, "x");

        Assert.Equal(2, store.Clear());
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Entries_SurviveDisposeAndReload()
    {
        var paths = new MockApplicationPaths();
        var item = MakeSeries("Persistent Anime", 2020);

        using (var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance))
        {
            store.Record(item, FailedItemReason.DownloadFailed, "disk full");
        }

        using var reloaded = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        var all = reloaded.GetAll();
        Assert.Single(all);
        Assert.Equal("Persistent Anime", all[0].Name);
        Assert.Equal(FailedItemReason.DownloadFailed, all[0].Reason);
        Assert.Equal("disk full", all[0].Error);
    }

    [Fact]
    public void Remove_AcceptsBothGuidFormats()
    {
        var paths = new MockApplicationPaths();
        using var store = new FailedItemsStore(paths, NullLogger<FailedItemsStore>.Instance);
        var item = MakeSeries("Format Anime", null);
        store.Record(item, FailedItemReason.Unresolved, null);

        // Dashboard sends the N-format string Jellyfin uses in its web URLs.
        Assert.True(store.Remove(item.Id.ToString("N")));
    }

    private static Series MakeSeries(string name, int? year)
    {
        return new Series
        {
            Id = Guid.NewGuid(),
            Name = name,
            ProductionYear = year
        };
    }

    private sealed class MockApplicationPaths : MediaBrowser.Common.Configuration.IApplicationPaths
    {
        public MockApplicationPaths()
        {
            PluginConfigurationsPath = Path.Combine(Path.GetTempPath(), "kometathemes_failed_test_" + Guid.NewGuid().ToString("N"));
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
