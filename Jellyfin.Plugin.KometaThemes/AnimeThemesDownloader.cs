using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Exceptions;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using MediaType = Jellyfin.Plugin.KometaThemes.Models.MediaType;

#pragma warning disable SA1611, SA1615, CA1305, CA3003, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Downloads anime theme songs and videos with multi-season support.
/// </summary>
public class AnimeThemesDownloader : IDisposable
{
    private const string ThemeMusicFileName = "theme.mp3";
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";

    private readonly HttpClient _client;
    private readonly IAnimeResolver _resolver;
    private readonly ILogger<AnimeThemesDownloader> _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly SeasonDetector _seasonDetector;
    private readonly ThemeGrouper _themeGrouper;
    private readonly DownloadTracker _downloadTracker;
    private readonly Sync.DownloadMetrics _metrics;
    private readonly ThemeLinkRepairService _linkRepair;
    private SemaphoreSlim _downloadSemaphore = new(2, 2);

    /// <summary>
    /// Initializes a new instance of the <see cref="AnimeThemesDownloader"/> class.
    /// </summary>
    public AnimeThemesDownloader(
        IMediaEncoder mediaEncoder,
        IHttpClientFactory clientFactory,
        IAnimeResolver resolver,
        ILogger<AnimeThemesDownloader> logger,
        SeasonDetector seasonDetector,
        ThemeGrouper themeGrouper,
        DownloadTracker downloadTracker,
        Sync.DownloadMetrics metrics,
        ThemeLinkRepairService linkRepair)
    {
        _mediaEncoder = mediaEncoder;
        _resolver = resolver;
        _logger = logger;
        _seasonDetector = seasonDetector;
        _themeGrouper = themeGrouper;
        _downloadTracker = downloadTracker;
        _metrics = metrics;
        _linkRepair = linkRepair;
        _client = clientFactory.CreateClient("AnimeThemesCDN");
    }

    /// <summary>
    /// Recreates the download semaphore with the configured parallelism level.
    /// </summary>
    private void UpdateSemaphore(int degree)
    {
        var newDegree = Math.Clamp(degree, 1, 8);
        _downloadSemaphore.Dispose();
        _downloadSemaphore = new SemaphoreSlim(newDegree, newDegree);
    }

    /// <summary>
    /// Checks if this item should be processed.
    /// </summary>
    public bool ShouldUpdate(BaseItem item, PluginConfiguration configuration, bool? forceOverride = null)
    {
        if (item.GetBaseItemKind() != BaseItemKind.Series &&
            item.GetBaseItemKind() != BaseItemKind.Movie &&
            item.GetBaseItemKind() != BaseItemKind.Season)
        {
            return false;
        }

        bool force = forceOverride ?? configuration.ForceSync;
        return force || !IsSatisfied(item, configuration);
    }

    /// <summary>
    /// Resolves a list of BaseItems to a list of BaseItems with their corresponding anime object.
    /// </summary>
    public IAsyncEnumerable<ItemWithAnime> ResolveItems(BaseItem[] items, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        return _resolver.ResolveItemsAsync(items, configuration, cancellationToken);
    }

    /// <summary>
    /// Processes an item, downloading themes for all applicable seasons.
    /// Returns true if any changes were made.
    /// </summary>
    public async ValueTask<bool> HandleAsync(BaseItem item, Anime anime, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[{Id}] Processing themes for: {Name} (AnimeId={AniId})", item.Id, item.Name, anime.Id);

        UpdateSemaphore(configuration.DegreeOfParallelism);

        var appliedConfiguration = ApplyFallbackMode(item, configuration);

        var isMovie = item.GetBaseItemKind() == BaseItemKind.Movie;

        var results = new List<bool>();

        if (isMovie)
        {
            var settings = appliedConfiguration.MovieSettings;
            results.Add(await ProcessMediaType(MediaType.Video, anime, item, appliedConfiguration.ForceSync, settings, null, cancellationToken).ConfigureAwait(false));
            results.Add(await ProcessMediaType(MediaType.Audio, anime, item, appliedConfiguration.ForceSync, settings, null, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            results.AddRange(await ProcessSeasons(anime, item, appliedConfiguration, cancellationToken).ConfigureAwait(false));
        }

        if (results.Any(r => r) || appliedConfiguration.ForceSync)
        {
            _logger.LogInformation("[{Id}] Saving metadata after theme changes with full refresh", item.Id);
            CopyBestThemeToRoot(item);
            var options = new MetadataRefreshOptions(new MediaBrowser.Controller.Providers.DirectoryService(BaseItem.FileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ForceSave = true,
                ReplaceAllMetadata = true
            };
            await item.RefreshMetadata(options, cancellationToken).ConfigureAwait(false);

            // Jellyfin 10.11.x may misfile the refreshed theme items under the
            // CollectionFolder — relink them deterministically to this item.
            try
            {
                await _linkRepair.RepairAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Theme link repair failed", item.Id);
            }

            return results.Any(r => r);
        }

        _logger.LogInformation("[{Id}] Finished without changes", item.Id);
        return false;
    }

    /// <summary>
    /// Copies the first available theme-music/*.mp3 as theme.mp3 in the item root folder.
    /// This provides a guaranteed fallback that Jellyfin 10.11.x always recognizes,
    /// even when its ThemeMediaResolver misfiles items under CollectionFolder.
    /// </summary>
    private void CopyBestThemeToRoot(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return;
        }

        try
        {
            var musicDir = Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory);
            var rootPath = Path.Combine(item.ContainingFolderPath, ThemeMusicFileName);

            if (!Directory.Exists(musicDir))
            {
                return;
            }

            var mp3Files = Directory.GetFiles(musicDir, "*.mp3");
            if (mp3Files.Length == 0)
            {
                return;
            }

            // Pick the first file and copy to theme.mp3
            var bestFile = mp3Files[0];
            File.Copy(bestFile, rootPath, overwrite: true);
            _logger.LogInformation("[{Id}] Copied {Src} → theme.mp3 for root-level theme song", item.Id, Path.GetFileName(bestFile));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to copy theme.mp3 to root", item.Id);
        }
    }

    private async Task<List<bool>> ProcessSeasons(Anime anime, BaseItem item, PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var results = new List<bool>();
        var seasonNumber = _seasonDetector.DetectSeason(item, configuration.SeasonDetectionMode);

        if (item is Season season)
        {
            _logger.LogInformation("[{Id}] Processing Season item: {Name} (Season={S})", item.Id, season.Name, seasonNumber);
        }
        else
        {
            _logger.LogInformation("[{Id}] Processing Series item: {Name} (DetectedSeason={S})", item.Id, item.Name, seasonNumber);
        }

        results.Add(await ProcessMediaType(MediaType.Video, anime, item, configuration.ForceSync, new CollectionTypeConfiguration { AudioSettings = configuration.AudioSettings, VideoSettings = configuration.VideoSettings, MaxThemesPerSeason = configuration.MaxThemesPerSeason }, seasonNumber, cancellationToken).ConfigureAwait(false));
        results.Add(await ProcessMediaType(MediaType.Audio, anime, item, configuration.ForceSync, new CollectionTypeConfiguration { AudioSettings = configuration.AudioSettings, VideoSettings = configuration.VideoSettings, MaxThemesPerSeason = configuration.MaxThemesPerSeason }, seasonNumber, cancellationToken).ConfigureAwait(false));

        if (item is Series series)
        {
            var childSeasons = series.Children.OfType<Season>().ToList();
            if (childSeasons.Count > 0)
            {
                _logger.LogInformation("[{Id}] Found {Count} child seasons to process", item.Id, childSeasons.Count);
                foreach (var childSeason in childSeasons)
                {
                    var childSeasonNumber = _seasonDetector.DetectSeason(childSeason, configuration.SeasonDetectionMode);
                    results.Add(await ProcessMediaType(MediaType.Video, anime, childSeason, configuration.ForceSync, new CollectionTypeConfiguration { AudioSettings = configuration.AudioSettings, VideoSettings = configuration.VideoSettings, MaxThemesPerSeason = configuration.MaxThemesPerSeason }, childSeasonNumber, cancellationToken).ConfigureAwait(false));
                    results.Add(await ProcessMediaType(MediaType.Audio, anime, childSeason, configuration.ForceSync, new CollectionTypeConfiguration { AudioSettings = configuration.AudioSettings, VideoSettings = configuration.VideoSettings, MaxThemesPerSeason = configuration.MaxThemesPerSeason }, childSeasonNumber, cancellationToken).ConfigureAwait(false));
                }
            }
        }

        return results;
    }

    private async ValueTask<bool> ProcessMediaType(
        MediaType type,
        Anime anime,
        BaseItem item,
        bool forceSync,
        CollectionTypeConfiguration configuration,
        int? seasonNumber,
        CancellationToken cancellationToken = default)
    {
        var settings = type == MediaType.Audio ? configuration.AudioSettings : configuration.VideoSettings;

        if (settings.FetchType == FetchType.None)
        {
            return false;
        }

        var allThemes = GetBestThemes(anime, settings).DistinctBy(it => it.Theme.Id).ToList();

        List<FlattenedTheme> themesToDownload;

        if (seasonNumber.HasValue)
        {
            var groups = _themeGrouper.GroupThemesBySeason(allThemes);
            var matchingGroup = _themeGrouper.FindMatchingGroup(groups, seasonNumber.Value);
            themesToDownload = matchingGroup?.Themes.ToList() ?? allThemes;
            _logger.LogInformation("[{Id}] Season {S}: {Count} themes available", item.Id, seasonNumber.Value, themesToDownload.Count);
        }
        else
        {
            themesToDownload = allThemes;
        }

        themesToDownload = PickThemes(settings.FetchType, themesToDownload, configuration.MaxThemesPerSeason).ToList();

        var links = ExtractLinks(type, themesToDownload, settings, seasonNumber).ToArray();

        if (forceSync)
        {
            if (type == MediaType.Audio)
            {
                RemoveFile(item, ThemeMusicFileName);
            }

            // Remove existing targets so they are actually re-downloaded,
            // not skipped because File.Exists still matches the new name.
            foreach (var link in links)
            {
                RemoveFile(item, link.Filepath);
            }

            CleanDirectory(item, type, links.Select(it => Path.GetFileName(it.Filepath)));
        }
        else
        {
            await PruneOrphanedFilesAsync(item, type, links.Select(it => Path.GetFileName(it.Filepath)), cancellationToken).ConfigureAwait(false);
        }

        bool changesMade = false;

        if (links.Length > 1)
        {
            var tasks = links.Select(async link =>
            {
                await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (await Download(type, link.Url, item, link.Filepath, settings.Volume, link.Theme, seasonNumber, cancellationToken).ConfigureAwait(false))
                    {
                        Interlocked.Exchange(ref changesMade, true);
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            foreach (var link in links)
            {
                changesMade |= await Download(type, link.Url, item, link.Filepath, settings.Volume, link.Theme, seasonNumber, cancellationToken).ConfigureAwait(false);
            }
        }

        return changesMade;
    }

    private List<FlattenedTheme> PickThemes(FetchType fetchType, List<FlattenedTheme> themes, int? maxThemes = null)
    {
        var selected = fetchType switch
        {
            FetchType.None => new List<FlattenedTheme>(),
            FetchType.Single => themes.Take(1).ToList(),
            FetchType.All => themes.ToList(),
            FetchType.AllPerSeason => themes.ToList(),
            _ => throw new ArgumentOutOfRangeException($"Unknown fetch type: {fetchType}")
        };

        if (maxThemes.HasValue && maxThemes.Value > 0 && selected.Count > maxThemes.Value)
        {
            selected = selected.Take(maxThemes.Value).ToList();
        }

        return selected;
    }

    private IEnumerable<(string Url, string Filepath, FlattenedTheme Theme)> ExtractLinks(
        MediaType type,
        List<FlattenedTheme> themes,
        MediaTypeConfiguration settings,
        int? seasonNumber)
    {
        bool isAudio = type == MediaType.Audio;

        foreach (var theme in themes)
        {
            var link = isAudio ? theme.Audio.Link : theme.Video.Link;
            var fileName = BuildThemeFileName(theme, settings, type);
            var directory = isAudio ? ThemeMusicDirectory : ThemeVideoDirectory;
            var path = Path.Combine(directory, fileName);

            yield return (link, path, theme);
        }
    }

    private string BuildThemeFileName(FlattenedTheme theme, MediaTypeConfiguration settings, MediaType type)
    {
        var typeStr = theme.Theme.Type == ThemeType.OP ? "OP" : "ED";
        var seq = theme.Theme.Sequence?.ToString() ?? "0";
        var slug = theme.Theme.Slug ?? "theme";

        var name = SlugToTitle(slug);

        var volume = (int)(settings.Volume * 100);
        var ext = type == MediaType.Audio ? "mp3" : "webm";

        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 60)
        {
            return $"{typeStr}{seq} - {name}__{volume}.{ext}";
        }

        return $"{typeStr}{seq}__{volume}.{ext}";
    }

    private static string SlugToTitle(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return string.Empty;
        }

        var words = slug.Split('-');
        var titleWords = new List<string>();
        foreach (var word in words)
        {
            if (word.Length == 0)
            {
                continue;
            }

            if (word.Length == 1)
            {
                titleWords.Add(word.ToUpperInvariant());
            }
            else
            {
                titleWords.Add(char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
            }
        }

        return string.Join(" ", titleWords);
    }

    private void RemoveFile(BaseItem series, string filename)
    {
        if (string.IsNullOrWhiteSpace(series.ContainingFolderPath))
        {
            // Virtual seasons (e.g. "Specials") have no folder of their own — routine, not a problem.
            _logger.LogDebug("[{Id}] Cannot remove file for item with null path: {Name}", series.Id, series.Name);
            return;
        }

        var path = Path.Combine(series.ContainingFolderPath, filename);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to delete file {Path}", series.Id, path);
        }
    }

    private void CleanDirectory(BaseItem series, MediaType mediaType, IEnumerable<string> allowedNames)
    {
        if (string.IsNullOrWhiteSpace(series.ContainingFolderPath))
        {
            _logger.LogDebug("[{Id}] Cannot clean directory for item with null path: {Name}", series.Id, series.Name);
            return;
        }

        var directory = mediaType == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory;
        var searchPattern = mediaType == MediaType.Audio ? "*.mp3" : "*.webm";

        var path = Path.Combine(series.ContainingFolderPath, directory);
        if (!Directory.Exists(path))
        {
            return;
        }

        var allowedNamesSet = allowedNames.ToHashSet();
        var removed = new List<string>();

        foreach (var filepath in Directory.GetFiles(path, searchPattern))
        {
            var name = Path.GetFileName(filepath);
            if (!allowedNamesSet.Contains(name))
            {
                _logger.LogInformation("[{Id}] Removing obsolete theme: {Theme}", series.Id, filepath);
                try
                {
                    File.Delete(filepath);
                    removed.Add(name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{Id}] Failed to delete obsolete theme {Path}", series.Id, filepath);
                }
            }
        }

        if (removed.Count > 0)
        {
            _ = _downloadTracker.RemoveRecordsAsync(series.ContainingFolderPath, removed);
        }
    }

    private async Task PruneOrphanedFilesAsync(BaseItem item, MediaType mediaType, IEnumerable<string> allowedNames, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var directory = mediaType == MediaType.Audio ? ThemeMusicDirectory : ThemeVideoDirectory;
        var searchPattern = mediaType == MediaType.Audio ? "*.mp3" : "*.webm";
        var path = Path.Combine(item.ContainingFolderPath, directory);
        if (!Directory.Exists(path))
        {
            return;
        }

        var existing = (await _downloadTracker.LoadAsync(item.ContainingFolderPath).ConfigureAwait(false))
            .ToDictionary(r => r.FileName, StringComparer.OrdinalIgnoreCase);
        var allowedSet = allowedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();

        foreach (var filepath in Directory.GetFiles(path, searchPattern))
        {
            var name = Path.GetFileName(filepath);
            if (allowedSet.Contains(name))
            {
                continue;
            }

            // Only prune files that the tracker knows about — never delete files we did not create.
            if (!existing.ContainsKey(name))
            {
                continue;
            }

            try
            {
                File.Delete(filepath);
                removed.Add(name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to prune orphan theme {Path}", item.Id, filepath);
            }
        }

        if (removed.Count > 0)
        {
            await _downloadTracker.RemoveRecordsAsync(item.ContainingFolderPath, removed).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Downloads a single theme file. Public entry point for manual theme picker.
    /// </summary>
    public ValueTask<bool> DownloadSingle(
        MediaType type,
        string url,
        BaseItem item,
        string relativePath,
        double volume = 1.0,
        CancellationToken cancellationToken = default)
        => Download(type, url, item, relativePath, volume, null, null, cancellationToken);

    private async ValueTask<bool> Download(
        MediaType type,
        string url,
        BaseItem item,
        string relativePath,
        double volume = 1.0,
        FlattenedTheme? theme = null,
        int? seasonNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            _logger.LogDebug("[{Id}] Cannot download for item with null path: {Name}", item.Id, item.Name);
            return false;
        }

        var path = Path.Combine(item.ContainingFolderPath, relativePath);
        if (File.Exists(path))
        {
            RemoveLegacyMutedVideoForTarget(type, path, volume, item);
            _metrics.RecordSkipped();
            return false;
        }

        var tempFile = Path.GetTempFileName();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            _logger.LogInformation("[{Id}] Downloading {Url} to {Path}", item.Id, url, path);
            using var downloadStream = await _client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            using var fileStream = File.OpenWrite(tempFile);
            await downloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    FileName = _mediaEncoder.EncoderPath,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    ErrorDialog = false,
                    ArgumentList = { "-i", tempFile }
                },
                EnableRaisingEvents = true
            };

            var arguments = process.StartInfo.ArgumentList;
            if (type == MediaType.Video)
            {
                arguments.Add("-c:v");
                arguments.Add("copy");
            }

            if (volume < 0.01 && type == MediaType.Video)
            {
                arguments.Add("-an");
            }
            else
            {
                arguments.Add("-filter:a");
                arguments.Add(string.Create(CultureInfo.InvariantCulture, $"volume={volume:0.00}"));
            }

            arguments.Add(path);

            process.Start();

            try
            {
                var errorRead = process.StandardError.ReadToEndAsync(cancellationToken);
                var exitWait = process.WaitForExitAsync(cancellationToken);

                // ffmpeg timeout from plugin configuration
                var timeoutSeconds = Math.Clamp(Plugin.Instance?.Configuration?.DownloadTimeoutSeconds ?? 60, 15, 300);
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var completedTask = await Task.WhenAny(exitWait, Task.Delay(Timeout.Infinite, linkedCts.Token)).ConfigureAwait(false);
                if (completedTask != exitWait)
                {
                    _logger.LogWarning("[{Id}] ffmpeg timed out, killing process", item.Id);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    throw new ConversionException(0, $"ffmpeg timed out after {timeoutSeconds}s");
                }

                var error = await errorRead.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    var commandInfo = $"Command line: {process.StartInfo.FileName} {string.Join(" ", arguments)}";
                    throw new ConversionException(process.ExitCode, commandInfo + "\n" + error);
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    _logger.LogWarning("[{Id}] ffmpeg cancelled, killing process", item.Id);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }

                throw;
            }

            _logger.LogInformation("[{Id}] Successfully downloaded theme!", item.Id);
            RemoveLegacyMutedVideoForTarget(type, path, volume, item);

            if (theme != null && !string.IsNullOrWhiteSpace(item.ContainingFolderPath))
            {
                try
                {
                    await _downloadTracker.AddRecordAsync(
                        item.ContainingFolderPath,
                        new DownloadRecord
                        {
                            ThemeId = theme.Theme.Id,
                            Type = theme.Theme.Type,
                            Sequence = theme.Theme.Sequence ?? 0,
                            Slug = theme.Theme.Slug ?? string.Empty,
                            FileName = Path.GetFileName(relativePath),
                            SeasonNumber = seasonNumber ?? 0,
                            DownloadedAt = DateTime.UtcNow,
                            ItemId = item.Id
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[{Id}] Failed to record download tracker entry for {Path}", item.Id, path);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Download failed");
            _metrics.RecordFailure();
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp file {Path}", tempFile);
            }
        }

        _metrics.RecordSuccess();
        return true;
    }

    private bool IsSatisfied(BaseItem item, PluginConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return false; // virtual items are never satisfied
        }

        var isMovie = item.GetBaseItemKind() == BaseItemKind.Movie;
        var audioSettings = isMovie ? configuration.MovieSettings.AudioSettings : configuration.AudioSettings;
        var videoSettings = isMovie ? configuration.MovieSettings.VideoSettings : configuration.VideoSettings;

        var audioSatisfied = item.GetThemeSongs().Any() || audioSettings.FetchType == FetchType.None;
        var videoSatisfied = videoSettings.FetchType == FetchType.None ||
            item.GetThemeVideos().Any(video => IsUsableThemeVideo(video.Path, videoSettings.Volume));

        // Jellyfin 10.11.x bug: ThemeMediaResolver may assign themes to CollectionFolder
        // instead of the individual Series. Fall back to filesystem check so the plugin
        // does not attempt a redundant download on every sync cycle.
        if (!audioSatisfied && audioSettings.FetchType != FetchType.None)
        {
            var musicDir = Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory);
            audioSatisfied = Directory.Exists(musicDir) && Directory.GetFiles(musicDir, "*.mp3").Length > 0;
        }

        if (!videoSatisfied && videoSettings.FetchType != FetchType.None)
        {
            var vidDir = Path.Combine(item.ContainingFolderPath, ThemeVideoDirectory);
            videoSatisfied = Directory.Exists(vidDir) && Directory.GetFiles(vidDir, "*.webm").Length > 0;
        }

        return audioSatisfied && videoSatisfied;
    }

    private void RemoveLegacyMutedVideoForTarget(MediaType type, string targetPath, double volume, BaseItem item)
    {
        if (type != MediaType.Video || volume < 0.01)
        {
            return;
        }

        var directory = Path.GetDirectoryName(targetPath);
        var fileName = Path.GetFileNameWithoutExtension(targetPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var suffixIndex = fileName.LastIndexOf("__", StringComparison.Ordinal);
        if (suffixIndex < 0)
        {
            return;
        }

        var legacyPath = Path.Combine(directory, fileName[..suffixIndex] + "__0.webm");
        if (!string.Equals(legacyPath, targetPath, StringComparison.Ordinal) && File.Exists(legacyPath))
        {
            _logger.LogInformation("[{Id}] Removing legacy muted video theme: {Theme}", item.Id, legacyPath);
            try
            {
                File.Delete(legacyPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Id}] Failed to delete legacy muted video {Path}", item.Id, legacyPath);
            }
        }
    }

    /// <summary>
    /// Returns a modified configuration when the item has no themes and a fallback mode is set.
    /// </summary>
    private static PluginConfiguration ApplyFallbackMode(BaseItem item, PluginConfiguration configuration)
    {
        if (configuration.MissingThemeFallbackMode == MissingThemeFallbackMode.None)
        {
            return configuration;
        }

        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return configuration;
        }

        var hasAnyThemeFile = Directory.Exists(Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory)) &&
                              Directory.GetFiles(Path.Combine(item.ContainingFolderPath, ThemeMusicDirectory), "*.mp3").Length > 0;
        var hasAnyVideoFile = Directory.Exists(Path.Combine(item.ContainingFolderPath, ThemeVideoDirectory)) &&
                              Directory.GetFiles(Path.Combine(item.ContainingFolderPath, ThemeVideoDirectory), "*.webm").Length > 0;

        if (hasAnyThemeFile && hasAnyVideoFile)
        {
            return configuration;
        }

        var clone = CloneConfigForFallback(configuration);
        var mode = configuration.MissingThemeFallbackMode;

        switch (mode)
        {
            case MissingThemeFallbackMode.AllOPs:
                clone.AudioSettings.FetchType = FetchType.All;
                clone.AudioSettings.IgnoreOPs = false;
                clone.AudioSettings.IgnoreEDs = true;
                clone.VideoSettings.FetchType = FetchType.None;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.All;
                clone.MovieSettings.AudioSettings.IgnoreOPs = false;
                clone.MovieSettings.AudioSettings.IgnoreEDs = true;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.None;
                break;

            case MissingThemeFallbackMode.AllEDs:
                clone.AudioSettings.FetchType = FetchType.All;
                clone.AudioSettings.IgnoreOPs = true;
                clone.AudioSettings.IgnoreEDs = false;
                clone.VideoSettings.FetchType = FetchType.None;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.All;
                clone.MovieSettings.AudioSettings.IgnoreOPs = true;
                clone.MovieSettings.AudioSettings.IgnoreEDs = false;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.None;
                break;

            case MissingThemeFallbackMode.AllVideos:
                clone.AudioSettings.FetchType = FetchType.None;
                clone.VideoSettings.FetchType = FetchType.All;
                clone.VideoSettings.IgnoreOPs = false;
                clone.VideoSettings.IgnoreEDs = false;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.None;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.All;
                clone.MovieSettings.VideoSettings.IgnoreOPs = false;
                clone.MovieSettings.VideoSettings.IgnoreEDs = false;
                break;

            case MissingThemeFallbackMode.AllOPsEDsVideos:
                clone.AudioSettings.FetchType = FetchType.All;
                clone.AudioSettings.IgnoreOPs = false;
                clone.AudioSettings.IgnoreEDs = false;
                clone.VideoSettings.FetchType = FetchType.All;
                clone.VideoSettings.IgnoreOPs = false;
                clone.VideoSettings.IgnoreEDs = false;
                clone.MovieSettings.AudioSettings.FetchType = FetchType.All;
                clone.MovieSettings.AudioSettings.IgnoreOPs = false;
                clone.MovieSettings.AudioSettings.IgnoreEDs = false;
                clone.MovieSettings.VideoSettings.FetchType = FetchType.All;
                clone.MovieSettings.VideoSettings.IgnoreOPs = false;
                clone.MovieSettings.VideoSettings.IgnoreEDs = false;
                break;
        }

        return clone;
    }

    private static PluginConfiguration CloneConfigForFallback(PluginConfiguration source)
    {
        return new PluginConfiguration
        {
            DegreeOfParallelism = source.DegreeOfParallelism,
            ForceSync = source.ForceSync,
            DryRunMode = source.DryRunMode,
            MaxThemesPerSeason = source.MaxThemesPerSeason,
            SeasonDetectionMode = source.SeasonDetectionMode,
            AudioSettings = new MediaTypeConfiguration
            {
                FetchType = source.AudioSettings.FetchType,
                IgnoreOverlapping = source.AudioSettings.IgnoreOverlapping,
                IgnoreEDs = source.AudioSettings.IgnoreEDs,
                IgnoreOPs = source.AudioSettings.IgnoreOPs,
                IgnoreThemesWithCredits = source.AudioSettings.IgnoreThemesWithCredits,
                Volume = source.AudioSettings.Volume
            },
            VideoSettings = new MediaTypeConfiguration
            {
                FetchType = source.VideoSettings.FetchType,
                IgnoreOverlapping = source.VideoSettings.IgnoreOverlapping,
                IgnoreEDs = source.VideoSettings.IgnoreEDs,
                IgnoreOPs = source.VideoSettings.IgnoreOPs,
                IgnoreThemesWithCredits = source.VideoSettings.IgnoreThemesWithCredits,
                Volume = source.VideoSettings.Volume
            },
            MovieSettings = new CollectionTypeConfiguration
            {
                AudioSettings = new MediaTypeConfiguration
                {
                    FetchType = source.MovieSettings.AudioSettings.FetchType,
                    IgnoreOverlapping = source.MovieSettings.AudioSettings.IgnoreOverlapping,
                    IgnoreEDs = source.MovieSettings.AudioSettings.IgnoreEDs,
                    IgnoreOPs = source.MovieSettings.AudioSettings.IgnoreOPs,
                    IgnoreThemesWithCredits = source.MovieSettings.AudioSettings.IgnoreThemesWithCredits,
                    Volume = source.MovieSettings.AudioSettings.Volume
                },
                VideoSettings = new MediaTypeConfiguration
                {
                    FetchType = source.MovieSettings.VideoSettings.FetchType,
                    IgnoreOverlapping = source.MovieSettings.VideoSettings.IgnoreOverlapping,
                    IgnoreEDs = source.MovieSettings.VideoSettings.IgnoreEDs,
                    IgnoreOPs = source.MovieSettings.VideoSettings.IgnoreOPs,
                    IgnoreThemesWithCredits = source.MovieSettings.VideoSettings.IgnoreThemesWithCredits,
                    Volume = source.MovieSettings.VideoSettings.Volume
                }
            },
            MissingThemeFallbackMode = source.MissingThemeFallbackMode
        };
    }

    private static bool IsUsableThemeVideo(string? path, double configuredVolume)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return configuredVolume < 0.01 ||
            !Path.GetFileName(path).EndsWith("__0.webm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets themes roughly sorted by relevance and filtered as needed.
    /// </summary>
    private IEnumerable<FlattenedTheme> GetBestThemes(Anime anime, MediaTypeConfiguration settings)
    {
        return (anime.Themes ?? [])
            .Where(theme => theme?.Entries != null)
            .SelectMany(theme => theme.Entries
                .Where(entry => entry?.Videos != null)
                .SelectMany(entry => entry.Videos
                    .Where(video => video?.Audio != null &&
                                    !string.IsNullOrWhiteSpace(video.Link) &&
                                    !string.IsNullOrWhiteSpace(video.Audio.Link))
                    .Select(video => Wrap(theme, entry, video))))
            .OrderBy(Rate)
            .ThenBy(t => t.Theme.Sequence ?? int.MaxValue)
            .Where(it => !settings.IgnoreOverlapping || it.Video.Overlap == OverlapType.None)
            .Where(it => !settings.IgnoreThemesWithCredits || it.Video.Creditless)
            .Where(it => !settings.IgnoreEDs || it.Theme.Type != ThemeType.ED)
            .Where(it => !settings.IgnoreOPs || it.Theme.Type != ThemeType.OP);
    }

    private static FlattenedTheme Wrap(AnimeTheme theme, AnimeThemeEntry entry, Models.Video video)
    {
        return new FlattenedTheme(theme, entry, video, video.Audio);
    }

    private static double Rate(FlattenedTheme theme)
    {
        double score = 0;

        if (theme.Entry.Nsfw)
        {
            score += 10;
        }

        if (theme.Entry.Spoiler)
        {
            score += 50;
        }

        switch (theme.Video.Overlap)
        {
            case OverlapType.Over:
                score += 20;
                break;
            case OverlapType.Transition:
                score += 15;
                break;
        }

        switch (theme.Video.Source)
        {
            case VideoSource.LD:
            case VideoSource.VHS:
                score += 10;
                break;
            case VideoSource.WEB:
            case VideoSource.RAW:
                score += 5;
                break;
        }

        if (!theme.Video.Creditless)
        {
            score += 10;
        }

        return score;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client.Dispose();
            _downloadSemaphore.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
