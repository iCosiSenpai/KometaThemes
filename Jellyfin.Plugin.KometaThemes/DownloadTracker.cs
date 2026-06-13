using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1001, CA1002, CA3003, CA1869, SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Tracks downloaded themes per item via JSON files stored alongside the media.
/// </summary>
public class DownloadTracker
{
    private const string TrackerFileName = "_kometa_themes.json";
    private readonly ILogger<DownloadTracker> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DownloadTracker(ILogger<DownloadTracker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the tracker file path for an item.
    /// </summary>
    public static string GetTrackerPath(string containingFolderPath)
        => Path.Combine(containingFolderPath, TrackerFileName);

    /// <summary>
    /// Loads download records for an item (thread-safe).
    /// </summary>
    public async Task<List<DownloadRecord>> LoadAsync(string containingFolderPath)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync(containingFolderPath).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Loads download records for an item (caller must hold lock).
    /// </summary>
    private async Task<List<DownloadRecord>> LoadUnlockedAsync(string containingFolderPath)
    {
        try
        {
            var path = GetTrackerPath(containingFolderPath);
            if (!File.Exists(path))
            {
                return new List<DownloadRecord>();
            }

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<DownloadRecord>>(json) ?? new List<DownloadRecord>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load download tracker for {Path}", containingFolderPath);
            return new List<DownloadRecord>();
        }
    }

    /// <summary>
    /// Saves download records for an item (caller must hold lock).
    /// </summary>
    private async Task SaveUnlockedAsync(string containingFolderPath, List<DownloadRecord> records)
    {
        try
        {
            var path = GetTrackerPath(containingFolderPath);
            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            _logger.LogDebug("Saved {Count} download records for {Path}", records.Count, containingFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save download tracker for {Path}", containingFolderPath);
        }
    }

    /// <summary>
    /// Saves download records for an item (thread-safe).
    /// </summary>
    public async Task SaveAsync(string containingFolderPath, List<DownloadRecord> records)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveUnlockedAsync(containingFolderPath, records).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Adds a download record and saves (atomic read-modify-write).
    /// </summary>
    public async Task AddRecordAsync(string containingFolderPath, DownloadRecord record)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await LoadUnlockedAsync(containingFolderPath).ConfigureAwait(false);
            records.RemoveAll(r => r.ThemeId == record.ThemeId);
            records.Add(record);
            await SaveUnlockedAsync(containingFolderPath, records).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes download records for specific files and saves (atomic read-modify-write).
    /// </summary>
    public async Task RemoveRecordsAsync(string containingFolderPath, IEnumerable<string> fileNames)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var records = await LoadUnlockedAsync(containingFolderPath).ConfigureAwait(false);
            var nameSet = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            records.RemoveAll(r => nameSet.Contains(r.FileName));
            await SaveUnlockedAsync(containingFolderPath, records).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets all downloaded theme files across all tracked items in a root path (for playlist).
    /// </summary>
    public async Task<List<string>> GetAllThemeFilesAsync(string rootPath)
    {
        var allFiles = new List<string>();
        try
        {
            if (!Directory.Exists(rootPath))
            {
                return allFiles;
            }

            foreach (var dir in Directory.GetDirectories(rootPath))
            {
                var trackerPath = GetTrackerPath(dir);
                if (!File.Exists(trackerPath))
                {
                    continue;
                }

                var records = await LoadAsync(dir).ConfigureAwait(false);
                foreach (var record in records)
                {
                    var fullPath = Path.Combine(dir, "theme-music", record.FileName);
                    if (File.Exists(fullPath))
                    {
                        allFiles.Add(fullPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect theme files from {Path}", rootPath);
        }

        return allFiles;
    }
}
