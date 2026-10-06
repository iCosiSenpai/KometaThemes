using System.Xml.Serialization;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Tests;

/// <summary>
/// Loading 1.x configuration files and first-run defaults.
/// </summary>
public class ConfigurationTests
{
    // The owner's real 1.4.1 file, trimmed: settings 2.0 dropped are still in it and must be ignored.
    private const string Legacy = """
        <?xml version="1.0" encoding="utf-8"?>
        <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <DegreeOfParallelism>8</DegreeOfParallelism>
          <ForceSync>true</ForceSync>
          <AudioSettings>
            <FetchType>All</FetchType>
            <IgnoreOverlapping>true</IgnoreOverlapping>
            <IgnoreEDs>false</IgnoreEDs>
            <IgnoreOPs>false</IgnoreOPs>
            <IgnoreThemesWithCredits>false</IgnoreThemesWithCredits>
            <Volume>0.5</Volume>
          </AudioSettings>
          <VideoSettings>
            <FetchType>AllPerSeason</FetchType>
            <IgnoreOverlapping>true</IgnoreOverlapping>
            <IgnoreEDs>false</IgnoreEDs>
            <IgnoreOPs>false</IgnoreOPs>
            <IgnoreThemesWithCredits>true</IgnoreThemesWithCredits>
            <Volume>0.5</Volume>
          </VideoSettings>
          <MovieSettings><MaxThemesPerSeason>3</MaxThemesPerSeason></MovieSettings>
          <ProviderPriority><string>AniList</string><string>AniList</string><string>Bogus</string></ProviderPriority>
          <EnableTitleFallback>true</EnableTitleFallback>
          <TitleMatchThreshold>0.8</TitleMatchThreshold>
          <RateLimitPerMinute>300</RateLimitPerMinute>
          <LibraryPattern>Anime</LibraryPattern>
          <SyncIntervalHours>6</SyncIntervalHours>
          <DryRunMode>false</DryRunMode>
          <EnablePlaylist>false</EnablePlaylist>
          <MissingThemeFallbackMode>None</MissingThemeFallbackMode>
          <AutoSyncOnItemAdded>true</AutoSyncOnItemAdded>
          <CleanupThemesOnItemRemoved>true</CleanupThemesOnItemRemoved>
          <SkippedItems>
            <SkippedItemEntry><ItemId>EA9F78E5-9766-5CA8-4796-CC2CBB2BF181</ItemId><SkippedUtc>2026-01-01T00:00:00Z</SkippedUtc></SkippedItemEntry>
            <SkippedItemEntry><ItemId>ea9f78e597665ca84796cc2cbb2bf181</ItemId><SkippedUtc>2026-02-01T00:00:00Z</SkippedUtc></SkippedItemEntry>
          </SkippedItems>
          <ManualBindings>
            <ManualBindingEntry><ItemId>fcce4ac5-4da3-020f-92df-f4e1cbb1ec26</ItemId><AnimeId>1386</AnimeId><BoundAt>2026-03-01T00:00:00Z</BoundAt></ManualBindingEntry>
          </ManualBindings>
        </PluginConfiguration>
        """;

    private static PluginConfiguration Load(string xml)
    {
        using var reader = new StringReader(xml);
        return (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(reader)!;
    }

    [Fact]
    public void Upgrade_KeepsSettings_SkipsSetup_AndCleansUp()
    {
        var config = Load(Legacy);

        Assert.True(ConfigurationMigrator.Migrate(config));

        Assert.Equal(PluginConfiguration.CurrentSchemaVersion, config.SchemaVersion);
        Assert.Equal(PluginConfiguration.CurrentSchemaVersion, config.SetupCompletedVersion);
        Assert.Equal("Anime", config.LibraryPattern);
        Assert.Equal(FetchType.All, config.AudioSettings.FetchType);
        Assert.Equal(FetchType.All, config.VideoSettings.FetchType);
        Assert.True(config.VideoSettings.IgnoreThemesWithCredits);
        Assert.Equal(4, config.DegreeOfParallelism);
        Assert.Equal(90, config.RateLimitPerMinute);
        Assert.Equal(["AniList", "AniDB", "MyAnimeList", "Kitsu", "AniSearch"], config.ProviderPriority);
        Assert.Single(config.SkippedItems);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), config.SkippedItems[0].SkippedUtc.ToUniversalTime());
        Assert.Equal(1386, config.FindBinding(Guid.Parse("fcce4ac5-4da3-020f-92df-f4e1cbb1ec26"))!.AnimeId);
    }

    [Fact]
    public void Upgrade_IsIdempotent()
    {
        var config = Load(Legacy);
        ConfigurationMigrator.Migrate(config);
        Assert.False(ConfigurationMigrator.Migrate(config));
    }

    [Fact]
    public void NewInstall_ShowsSetup_AndHasSafeDefaults()
    {
        var config = new PluginConfiguration();

        ConfigurationMigrator.Migrate(config);

        Assert.Equal(0, config.SetupCompletedVersion);
        Assert.Null(config.LibraryPattern);
        Assert.Empty(config.LibraryIds);
        Assert.True(config.AudioSettings.Enabled);
        Assert.False(config.VideoSettings.Enabled);
        Assert.True(config.PerSeasonThemes);
        Assert.Equal(5, config.ProviderPriority.Count);
    }

    [Fact]
    public void Roundtrip_DoesNotGrowLists()
    {
        var config = new PluginConfiguration();
        ConfigurationMigrator.Migrate(config);
        config.LibraryIds.Add(Guid.NewGuid().ToString());

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        for (var i = 0; i < 3; i++)
        {
            using var writer = new StringWriter();
            serializer.Serialize(writer, config);
            config = Load(writer.ToString());
            ConfigurationMigrator.Migrate(config);
        }

        Assert.Single(config.LibraryIds);
        Assert.Equal(5, config.ProviderPriority.Count);
    }

    [Theory]
    [InlineData(-1.0, 0)]
    [InlineData(0.333, 33)]
    [InlineData(2.0, 100)]
    public void Volume_IsClampedAndRounded(double volume, int percent)
    {
        var config = new PluginConfiguration();
        config.AudioSettings.Volume = volume;
        config.NormalizeBounds();
        Assert.Equal(percent, config.AudioSettings.VolumePercent);
    }
}
