using System.Threading;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Thread-safe in-process download counters. Exposed via the Health endpoint.
/// </summary>
public sealed class DownloadMetrics
{
    private long _downloadSuccess;
    private long _downloadFailed;
    private long _downloadSkipped;

    /// <summary>
    /// Gets the number of themes downloaded successfully.
    /// </summary>
    public long DownloadSuccess => Interlocked.Read(ref _downloadSuccess);

    /// <summary>
    /// Gets the number of themes that failed to download.
    /// </summary>
    public long DownloadFailed => Interlocked.Read(ref _downloadFailed);

    /// <summary>
    /// Gets the number of themes skipped (already present or skipped by policy).
    /// </summary>
    public long DownloadSkipped => Interlocked.Read(ref _downloadSkipped);

    /// <summary>
    /// Records a successful download.
    /// </summary>
    public void RecordSuccess() => Interlocked.Increment(ref _downloadSuccess);

    /// <summary>
    /// Records a failed download.
    /// </summary>
    public void RecordFailure() => Interlocked.Increment(ref _downloadFailed);

    /// <summary>
    /// Records a skipped download.
    /// </summary>
    public void RecordSkipped() => Interlocked.Increment(ref _downloadSkipped);

    /// <summary>
    /// Resets all counters.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _downloadSuccess, 0);
        Interlocked.Exchange(ref _downloadFailed, 0);
        Interlocked.Exchange(ref _downloadSkipped, 0);
    }
}
