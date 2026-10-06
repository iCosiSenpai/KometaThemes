using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

// FolderResult is working data handed to the caller, not a public contract.
#pragma warning disable CA1002

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// A folder that receives theme files: a series, one of its seasons, or a movie.
/// </summary>
/// <param name="ItemId">The library item whose folder it is.</param>
/// <param name="Folder">Folder path.</param>
/// <param name="SeasonNumber">Season number, or 0 for a series or a movie.</param>
/// <param name="CleanLegacyRootCopy">Whether a 1.x <c>theme.mp3</c> copy may exist in this folder.</param>
public sealed record ThemeFolder(Guid ItemId, string Folder, int SeasonNumber, bool CleanLegacyRootCopy);

/// <summary>
/// What changed in a folder.
/// </summary>
public sealed class FolderResult
{
    /// <summary>Gets or sets files downloaded.</summary>
    public int Downloaded { get; set; }

    /// <summary>Gets or sets files renamed to the 2.0 naming.</summary>
    public int Renamed { get; set; }

    /// <summary>Gets or sets files removed.</summary>
    public int Deleted { get; set; }

    /// <summary>Gets or sets files that could not be downloaded.</summary>
    public int Failed { get; set; }

    /// <summary>Gets the error messages, one per failed file.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Gets the titles of downloaded files, for the activity list.</summary>
    public List<string> DownloadedNames { get; } = [];

    /// <summary>Gets a value indicating whether anything on disk changed.</summary>
    public bool Changed => Downloaded + Renamed + Deleted > 0;
}

/// <summary>
/// Brings a folder's theme files in line with a plan.
/// </summary>
public sealed class ThemeInstaller
{
    /// <summary>Name of the HTTP client for theme files.</summary>
    public const string HttpClientName = "AnimeThemesCDN";

    /// <summary>Largest theme file accepted.</summary>
    internal const long MaxDownloadBytes = 400L * 1024 * 1024;

    /// <summary>Suffix of a file being written. Never left behind.</summary>
    internal const string PartialSuffix = ".kt-part";

    private const string LegacyRootThemeName = "theme.mp3";
    private static readonly TimeSpan DownloadDeadline = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _clientFactory;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly TranscodeGate _transcodeGate;
    private readonly FolderStateStore _store;
    private readonly FolderLocks _locks;
    private readonly ILogger<ThemeInstaller> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeInstaller"/> class.
    /// </summary>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="mediaEncoder">Jellyfin's media encoder, for the ffmpeg path.</param>
    /// <param name="transcodeGate">Plugin-wide ffmpeg budget.</param>
    /// <param name="store">State file store.</param>
    /// <param name="locks">Folder locks.</param>
    /// <param name="logger">Logger.</param>
    public ThemeInstaller(
        IHttpClientFactory clientFactory,
        IMediaEncoder mediaEncoder,
        TranscodeGate transcodeGate,
        FolderStateStore store,
        FolderLocks locks,
        ILogger<ThemeInstaller> logger)
    {
        _clientFactory = clientFactory;
        _mediaEncoder = mediaEncoder;
        _transcodeGate = transcodeGate;
        _store = store;
        _locks = locks;
        _logger = logger;
    }

    /// <summary>
    /// Makes a folder hold exactly the themes the settings and the owner's choices ask for.
    /// </summary>
    /// <param name="target">The folder.</param>
    /// <param name="animes">Anime entries of the folder; empty when it should hold no themes of its own.</param>
    /// <param name="config">Settings.</param>
    /// <param name="redownload">Whether to fetch every wanted file again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What changed.</returns>
    public async Task<FolderResult> ApplyAsync(ThemeFolder target, IReadOnlyList<Anime> animes, PluginConfiguration config, bool redownload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(animes);
        ArgumentNullException.ThrowIfNull(config);

        var result = new FolderResult();
        using var folderLock = await _locks.AcquireAsync(target.Folder, cancellationToken).ConfigureAwait(false);

        var state = await _store.LoadAsync(target.Folder, cancellationToken).ConfigureAwait(false);
        var max = config.MaxThemesPerSeason;
        var catalog = ThemeCatalog.All(animes);
        var desired = ThemePlanner.Desired(
            ThemeCatalog.Select(animes, config.AudioSettings, max),
            ThemeCatalog.Select(animes, config.VideoSettings, max),
            catalog,
            state,
            config.AudioSettings.VolumePercent,
            config.VideoSettings.VolumePercent,
            ForeignNames(target.Folder, state));

        var plan = ThemePlanner.Build(state.Records, desired, (directory, name) => File.Exists(Path.Combine(target.Folder, directory, name)));
        if (redownload)
        {
            foreach (var (record, want) in plan.Keep.Concat(plan.Rename).ToList())
            {
                plan.Download.Add((want, record));
            }

            plan.Keep.Clear();
            plan.Rename.Clear();
        }

        foreach (var record in plan.Forget)
        {
            state.Records.Remove(record);
        }

        foreach (var record in plan.Delete)
        {
            if (TryDelete(Path.Combine(target.Folder, record.EffectiveDirectory, record.FileName)))
            {
                state.Records.Remove(record);
                result.Deleted++;
            }
        }

        RenameAll(target, plan, result);

        if (plan.Download.Count > 0)
        {
            await DownloadAllAsync(target, plan.Download, state, config, result, cancellationToken).ConfigureAwait(false);
        }

        if (target.CleanLegacyRootCopy)
        {
            result.Deleted += RemoveLegacyRootCopy(target.Folder, state);
        }

        state.ItemId = target.ItemId;
        state.AnimeIds = animes.Select(anime => anime.Id).ToList();
        state.CheckedUtc = DateTime.UtcNow;
        await SaveQuietlyAsync(target.Folder, state, cancellationToken).ConfigureAwait(false);

        RemoveEmptyThemeDirectories(target.Folder);
        return result;
    }

    /// <summary>
    /// Records the owner's choice for one theme in a folder.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <param name="themeId">Theme ID.</param>
    /// <param name="media">Media type.</param>
    /// <param name="keep">True to keep, false to hide, null to follow the settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SetOverrideAsync(string folder, int themeId, MediaType media, bool? keep, CancellationToken cancellationToken)
    {
        using var folderLock = await _locks.AcquireAsync(folder, cancellationToken).ConfigureAwait(false);
        var state = await _store.LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        state.SetOverride(themeId, media, keep);
        await _store.SaveAsync(folder, state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a file KometaThemes recorded. Files it did not write are refused.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <param name="directory">Theme folder name.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The removed record, or null when the file is not one KometaThemes wrote.</returns>
    public async Task<ThemeRecord?> DeleteRecordedFileAsync(string folder, string directory, string fileName, CancellationToken cancellationToken)
    {
        if (!ThemeFileKinds.IsThemeDirectory(directory) || !ThemeNaming.IsPlainFileName(fileName))
        {
            return null;
        }

        using var folderLock = await _locks.AcquireAsync(folder, cancellationToken).ConfigureAwait(false);
        var state = await _store.LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        var record = state.Records.FirstOrDefault(r =>
            string.Equals(r.EffectiveDirectory, directory, StringComparison.Ordinal)
            && string.Equals(r.FileName, fileName, StringComparison.Ordinal));
        if (record == null)
        {
            return null;
        }

        var path = Path.Combine(folder, record.EffectiveDirectory, record.FileName);
        if (File.Exists(path) && !TryDelete(path))
        {
            throw new IOException("The file could not be deleted.");
        }

        state.Records.Remove(record);
        if (record.Source == ThemeSource.AnimeThemes)
        {
            // Otherwise the next check would download it again.
            state.SetOverride(record.ThemeId, record.Media, false);
        }

        await _store.SaveAsync(folder, state, cancellationToken).ConfigureAwait(false);
        RemoveEmptyThemeDirectories(folder);
        return record;
    }

    /// <summary>
    /// Deletes every file KometaThemes wrote in a folder, and its state file.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <param name="itemId">Only records of this item are removed, for folders shared by several movies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many files were deleted.</returns>
    public async Task<int> DeleteAllAsync(string folder, Guid? itemId, CancellationToken cancellationToken)
    {
        using var folderLock = await _locks.AcquireAsync(folder, cancellationToken).ConfigureAwait(false);
        var state = await _store.LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        var deleted = 0;
        foreach (var record in state.Records.Where(r => itemId == null || r.ItemId == Guid.Empty || r.ItemId == itemId).ToList())
        {
            if (!ThemeNaming.IsPlainFileName(record.FileName))
            {
                continue;
            }

            var path = Path.Combine(folder, record.EffectiveDirectory, record.FileName);
            var existed = File.Exists(path);
            if (!existed || TryDelete(path))
            {
                deleted += existed ? 1 : 0;
                state.Records.Remove(record);
            }
        }

        deleted += RemoveLegacyRootCopy(folder, state);
        if (itemId == null || state.Records.Count == 0)
        {
            state.Overrides.Clear();
            state.AnimeIds.Clear();
        }

        await SaveQuietlyAsync(folder, state, cancellationToken).ConfigureAwait(false);
        RemoveEmptyThemeDirectories(folder);
        return deleted;
    }

    /// <summary>
    /// Converts a local media file (a YouTube download) into a theme in a folder.
    /// </summary>
    /// <param name="target">The folder.</param>
    /// <param name="sourceFile">Source media file.</param>
    /// <param name="media">Audio or video.</param>
    /// <param name="record">Record to store; its file name is chosen here.</param>
    /// <param name="baseName">File name without extension.</param>
    /// <param name="volume">Volume in percent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file name written.</returns>
    public async Task<string> ImportAsync(ThemeFolder target, string sourceFile, MediaType media, ThemeRecord record, string baseName, int volume, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(record);

        using var folderLock = await _locks.AcquireAsync(target.Folder, cancellationToken).ConfigureAwait(false);
        var state = await _store.LoadAsync(target.Folder, cancellationToken).ConfigureAwait(false);
        var directory = media == MediaType.Audio ? ThemeFileKinds.AudioDirectory : ThemeFileKinds.VideoDirectory;
        var extension = media == MediaType.Audio ? ".mp3" : NormalizeVideoExtension(Path.GetExtension(sourceFile));
        var taken = ExistingNames(Path.Combine(target.Folder, directory));
        var fileName = ThemeNaming.Unique(ThemeNaming.FileName(baseName, null, extension), taken);
        var path = Path.Combine(target.Folder, directory, fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await TranscodeAsync(media, sourceFile, path, volume, record.Title, record.Artists, null, 600, cancellationToken).ConfigureAwait(false);

        record.FileName = fileName;
        record.Directory = directory;
        record.Volume = volume;
        record.ItemId = target.ItemId;
        record.SeasonNumber = target.SeasonNumber;
        record.DownloadedAt = DateTime.UtcNow;
        state.Records.Add(record);
        state.ItemId = target.ItemId;
        await _store.SaveAsync(target.Folder, state, cancellationToken).ConfigureAwait(false);
        return fileName;
    }

    /// <summary>
    /// Picks the ffmpeg muxer for a target path; the partial suffix hides the extension from ffmpeg.
    /// </summary>
    /// <param name="media">Audio or video.</param>
    /// <param name="targetPath">Final path.</param>
    /// <returns>The muxer name.</returns>
    internal static string MuxerFor(MediaType media, string targetPath)
        => media == MediaType.Audio
            ? "mp3"
            : Path.GetExtension(targetPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "webm";

    private static string NormalizeVideoExtension(string extension)
        => ThemeFileKinds.IsVideoFile("x" + extension) ? extension.ToLowerInvariant() : ".webm";

    private static HashSet<string> ExistingNames(string directory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                names.Add(Path.GetFileName(path));
            }
        }

        return names;
    }

    private static Dictionary<string, HashSet<string>> ForeignNames(string folder, FolderState state)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var directory in new[] { ThemeFileKinds.AudioDirectory, ThemeFileKinds.VideoDirectory })
        {
            var managed = state.Records
                .Where(r => string.Equals(r.EffectiveDirectory, directory, StringComparison.Ordinal) && r.Source == ThemeSource.AnimeThemes)
                .Select(r => r.FileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var names = ExistingNames(Path.Combine(folder, directory));
            names.ExceptWith(managed);
            result[directory] = names;
        }

        return result;
    }

    private void RenameAll(ThemeFolder target, ThemePlan plan, FolderResult result)
    {
        // Two phases, so two files swapping names never collide.
        var staged = new List<(ThemeRecord Record, DesiredFile Want, string TempPath)>();
        foreach (var (record, want) in plan.Rename)
        {
            var from = Path.Combine(target.Folder, record.EffectiveDirectory, record.FileName);
            var temp = from + ".kt-rename";
            try
            {
                File.Move(from, temp, overwrite: false);
                staged.Add((record, want, temp));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not rename {Path}", from);
            }
        }

        foreach (var (record, want, temp) in staged)
        {
            var directory = want.Media == MediaType.Audio ? ThemeFileKinds.AudioDirectory : ThemeFileKinds.VideoDirectory;
            var destination = Path.Combine(target.Folder, directory, want.FileName);
            try
            {
                if (File.Exists(destination))
                {
                    // A file nobody recorded took the name in the meantime: keep both, never overwrite.
                    var free = ThemeNaming.Unique(want.FileName, ExistingNames(Path.GetDirectoryName(destination)!));
                    destination = Path.Combine(target.Folder, directory, free);
                }

                File.Move(temp, destination, overwrite: false);
                record.FileName = Path.GetFileName(destination);
                record.Directory = directory;
                Describe(record, want);
                result.Renamed++;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not rename {Path} to {Destination}", temp, destination);
                TryMoveBack(temp, Path.Combine(target.Folder, record.EffectiveDirectory, record.FileName));
            }
        }

        foreach (var (record, want) in plan.Keep)
        {
            // Fill in what 1.x records lack, so the owner sees song names.
            Describe(record, want);
        }
    }

    private async Task DownloadAllAsync(
        ThemeFolder target,
        List<(DesiredFile Desired, ThemeRecord? Replaces)> downloads,
        FolderState state,
        PluginConfiguration config,
        FolderResult result,
        CancellationToken cancellationToken)
    {
        using var parallel = new SemaphoreSlim(config.DegreeOfParallelism, config.DegreeOfParallelism);
        var outcomes = await Task.WhenAll(downloads.Select(async download =>
        {
            await parallel.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return (download.Desired, download.Replaces, Error: await DownloadOneAsync(target, download.Desired, download.Replaces, config, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                parallel.Release();
            }
        })).ConfigureAwait(false);

        foreach (var (want, replaces, error) in outcomes)
        {
            if (error != null)
            {
                result.Failed++;
                result.Errors.Add($"{want.Choice.Code} {want.Choice.Title}: {error}".Trim());
                continue;
            }

            if (replaces != null)
            {
                state.Records.Remove(replaces);
                if (!string.Equals(replaces.FileName, want.FileName, StringComparison.Ordinal) || !string.Equals(replaces.EffectiveDirectory, DirectoryOf(want.Media), StringComparison.Ordinal))
                {
                    TryDelete(Path.Combine(target.Folder, replaces.EffectiveDirectory, replaces.FileName));
                }
            }

            var record = new ThemeRecord
            {
                FileName = want.FileName,
                Directory = DirectoryOf(want.Media),
                SeasonNumber = target.SeasonNumber,
                DownloadedAt = DateTime.UtcNow,
                ItemId = target.ItemId,
                Source = ThemeSource.AnimeThemes,
                SourceUrl = want.Choice.Link(want.Media) ?? string.Empty,
                Volume = want.Volume,
            };
            Describe(record, want);
            state.Records.Add(record);
            result.Downloaded++;
            result.DownloadedNames.Add(want.Choice.Title ?? want.Choice.Code);
        }
    }

    private static string DirectoryOf(MediaType media) => media == MediaType.Audio ? ThemeFileKinds.AudioDirectory : ThemeFileKinds.VideoDirectory;

    private static void Describe(ThemeRecord record, DesiredFile want)
    {
        record.ThemeId = want.Choice.Theme.Id;
        record.Type = want.Choice.Theme.Type;
        record.Sequence = want.Choice.Theme.Sequence ?? 1;
        record.Slug = want.Choice.Theme.Slug ?? want.Choice.Code;
        record.AnimeId = want.Choice.Anime.Id;
        record.Title = want.Choice.Title;
        record.Artists = want.Choice.Artists.Length == 0 ? null : want.Choice.Artists;
        record.Volume ??= want.Volume;
    }

    /// <returns>Null on success, else a short reason.</returns>
    private async Task<string?> DownloadOneAsync(ThemeFolder target, DesiredFile want, ThemeRecord? replaces, PluginConfiguration config, CancellationToken cancellationToken)
    {
        var url = want.Choice.Link(want.Media);
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return "no download link";
        }

        var directory = DirectoryOf(want.Media);
        var finalPath = Path.Combine(target.Folder, directory, want.FileName);
        var sameFile = replaces != null
            && string.Equals(replaces.FileName, want.FileName, StringComparison.Ordinal)
            && string.Equals(replaces.EffectiveDirectory, directory, StringComparison.Ordinal);
        if (File.Exists(finalPath) && !sameFile)
        {
            // Names were made unique against every file present, so this only happens in a race with
            // someone writing the folder; never overwrite what we did not plan to replace.
            return "a file with the same name appeared";
        }

        var scratch = Path.Combine(Path.GetTempPath(), "kometathemes");
        Directory.CreateDirectory(scratch);
        var source = Path.Combine(scratch, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + Path.GetExtension(uri.AbsolutePath));
        try
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(DownloadDeadline);
                try
                {
                    await FetchAsync(uri, source, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return "download took longer than 10 minutes";
                }
            }

            Directory.CreateDirectory(Path.Combine(target.Folder, directory));
            await TranscodeAsync(want.Media, source, finalPath, want.Volume, want.Choice.Title, want.Choice.Artists, want.Choice.Anime.Name, config.DownloadTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Saved {File} in {Folder}", want.FileName, target.Folder);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not save {File} in {Folder}", want.FileName, target.Folder);
            return ex.Message;
        }
        finally
        {
            TryDelete(source);
        }
    }

    private async Task FetchAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new InvalidDataException("the file is larger than 400 MB");
        }

        var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (input.ConfigureAwait(false))
        {
            var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                    {
                        throw new InvalidDataException("the file is larger than 400 MB");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Runs ffmpeg into a partial file and moves it into place only after it is verified, so a failed
    /// run never leaves a broken theme that later checks would mistake for a finished one.
    /// </summary>
    private async Task TranscodeAsync(
        MediaType media,
        string sourceFile,
        string targetPath,
        int volume,
        string? title,
        string? artists,
        string? album,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var partial = targetPath + PartialSuffix;
        TryDelete(partial);
        using var slot = await _transcodeGate.AcquireAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _mediaEncoder.EncoderPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            var args = process.StartInfo.ArgumentList;
            foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", sourceFile, "-map_metadata", "-1" })
            {
                args.Add(arg);
            }

            if (media == MediaType.Video)
            {
                // The container follows the source, so the video stream is always copied, never re-encoded.
                args.Add("-c:v");
                args.Add("copy");
            }
            else
            {
                args.Add("-vn");
                args.Add("-c:a");
                args.Add("libmp3lame");
                args.Add("-q:a");
                args.Add("2");
            }

            if (media == MediaType.Video && volume <= 0)
            {
                args.Add("-an");
            }
            else
            {
                args.Add("-filter:a");
                args.Add(string.Create(CultureInfo.InvariantCulture, $"volume={volume / 100.0:0.00}"));
            }

            AddTag(args, "title", title);
            AddTag(args, "artist", artists);
            AddTag(args, "album", album);
            args.Add("-f");
            args.Add(MuxerFor(media, targetPath));
            args.Add(partial);

            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 15, 600)));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"ffmpeg did not finish within {timeoutSeconds} seconds"));
                }
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var error = (await stderr.ConfigureAwait(false)).Trim();
                throw new InvalidOperationException("ffmpeg failed: " + (error.Length > 300 ? error[..300] : error));
            }

            var info = new FileInfo(partial);
            if (!info.Exists || info.Length == 0)
            {
                throw new InvalidOperationException("ffmpeg produced no output");
            }

            // A stream copy of a source without video succeeds and yields an audio-only file, which in
            // backdrops/ is a theme video that shows nothing.
            if (media == MediaType.Video && !await HasVideoStreamAsync(partial, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("the video has no picture");
            }

            File.Move(partial, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static void AddTag(System.Collections.ObjectModel.Collection<string> args, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            args.Add("-metadata");
            args.Add(name + "=" + value.Trim());
        }
    }

    private async Task<bool> HasVideoStreamAsync(string path, CancellationToken cancellationToken)
    {
        var probe = _mediaEncoder.ProbePath;
        if (string.IsNullOrWhiteSpace(probe))
        {
            return true;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = probe,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_type", "-of", "csv=p=0", path }
            }
        };

        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 1.x left a hung ffprobe running here.
                Kill(process);
                return true;
            }

            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return (await stdout.ConfigureAwait(false)).Contains("video", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not check {Path} for a video stream", path);
            return true;
        }
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "Could not stop ffmpeg");
        }
    }

    /// <summary>
    /// Removes the <c>theme.mp3</c> copy 1.x placed in a series or movie folder, but only when it is
    /// byte-for-byte a song KometaThemes recorded: a <c>theme.mp3</c> the owner put there stays.
    /// </summary>
    private int RemoveLegacyRootCopy(string folder, FolderState state)
    {
        var rootCopy = new FileInfo(Path.Combine(folder, LegacyRootThemeName));
        if (!rootCopy.Exists)
        {
            return 0;
        }

        try
        {
            var candidates = state.Records
                .Where(r => r.Media == MediaType.Audio)
                .Select(r => new FileInfo(Path.Combine(folder, r.EffectiveDirectory, r.FileName)))
                .Where(f => f.Exists && f.Length == rootCopy.Length)
                .ToList();
            if (candidates.Count == 0)
            {
                return 0;
            }

            var rootHash = Hash(rootCopy.FullName);
            if (candidates.Any(c => Hash(c.FullName).AsSpan().SequenceEqual(rootHash)) && TryDelete(rootCopy.FullName))
            {
                _logger.LogInformation("Removed the duplicate theme.mp3 that KometaThemes 1.x copied into {Folder}", folder);
                return 1;
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not compare theme.mp3 in {Folder}", folder);
        }

        return 0;
    }

    private static byte[] Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private async Task SaveQuietlyAsync(string folder, FolderState state, CancellationToken cancellationToken)
    {
        try
        {
            await _store.SaveAsync(folder, state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the state file in {Folder}", folder);
        }
    }

    private void RemoveEmptyThemeDirectories(string folder)
    {
        foreach (var directory in new[] { ThemeFileKinds.AudioDirectory, ThemeFileKinds.VideoDirectory })
        {
            var path = Path.Combine(folder, directory);
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                }
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not remove empty folder {Path}", path);
            }
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete {Path}", path);
            return false;
        }
    }

    private void TryMoveBack(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not restore {Path}", to);
        }
    }
}
