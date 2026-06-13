using System;
using System.Threading;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Tracks live KometaThemes sync progress independently from Jellyfin scheduled-task progress.
/// </summary>
public sealed class SyncStatusTracker
{
    private readonly object _gate = new();
    private SyncStatusResponse _status = SyncStatusResponse.Idle();

    /// <summary>
    /// Gets the current status snapshot.
    /// </summary>
    /// <returns>Current sync status.</returns>
    public SyncStatusResponse GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    /// <summary>
    /// Starts a new status session.
    /// </summary>
    /// <param name="totalItems">Total candidate item count.</param>
    public void Start(int totalItems)
    {
        Update("scan", totalItems, 0, 0, 0, 0, null, false);
    }

    /// <summary>
    /// Updates the status snapshot.
    /// </summary>
    /// <param name="phase">Current phase.</param>
    /// <param name="totalItems">Total candidate item count.</param>
    /// <param name="processedItems">Processed item count.</param>
    /// <param name="resolvedItems">Resolved item count.</param>
    /// <param name="downloadedItems">Downloaded item count.</param>
    /// <param name="skippedItems">Skipped item count.</param>
    /// <param name="message">Optional display message.</param>
    /// <param name="isFinished">Whether the sync is finished.</param>
    public void Update(
        string phase,
        int totalItems,
        int processedItems,
        int resolvedItems,
        int downloadedItems,
        int skippedItems,
        string? message = null,
        bool isFinished = false)
    {
        lock (_gate)
        {
            _status = new SyncStatusResponse(
                phase,
                totalItems,
                processedItems,
                resolvedItems,
                downloadedItems,
                skippedItems,
                CalculateProgress(phase, totalItems, processedItems, resolvedItems, downloadedItems, isFinished),
                message ?? string.Empty,
                DateTime.UtcNow,
                isFinished);
        }
    }

    private static double CalculateProgress(
        string phase,
        int totalItems,
        int processedItems,
        int resolvedItems,
        int downloadedItems,
        bool isFinished)
    {
        if (isFinished)
        {
            return 100;
        }

        if (totalItems <= 0)
        {
            return 0;
        }

        var scanBase = phase switch
        {
            "scan" => 5,
            "filter" => 10,
            "resolve" => 10 + (resolvedItems * 60.0 / totalItems),
            "download" => 70 + (downloadedItems * 30.0 / Math.Max(1, Math.Max(resolvedItems, processedItems))),
            "failed" => 100,
            _ => processedItems * 100.0 / totalItems
        };

        return Math.Round(Math.Min(99, Math.Max(0, scanBase)), 1);
    }
}
