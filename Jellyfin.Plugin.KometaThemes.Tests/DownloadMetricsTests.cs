using System;
using Jellyfin.Plugin.KometaThemes.Sync;
using Xunit;

namespace Jellyfin.Plugin.KometaThemes.Tests;

public class DownloadMetricsTests
{
    [Fact]
    public void Initial_AllCounters_AreZero()
    {
        var metrics = new DownloadMetrics();
        Assert.Equal(0, metrics.DownloadSuccess);
        Assert.Equal(0, metrics.DownloadFailed);
        Assert.Equal(0, metrics.DownloadSkipped);
    }

    [Fact]
    public void RecordSuccess_Increments_Success()
    {
        var metrics = new DownloadMetrics();
        metrics.RecordSuccess();
        metrics.RecordSuccess();
        Assert.Equal(2, metrics.DownloadSuccess);
    }

    [Fact]
    public void RecordFailure_Increments_Failure()
    {
        var metrics = new DownloadMetrics();
        metrics.RecordFailure();
        Assert.Equal(1, metrics.DownloadFailed);
    }

    [Fact]
    public void RecordSkipped_Increments_Skipped()
    {
        var metrics = new DownloadMetrics();
        metrics.RecordSkipped();
        Assert.Equal(1, metrics.DownloadSkipped);
    }

    [Fact]
    public void Reset_Zeros_AllCounters()
    {
        var metrics = new DownloadMetrics();
        metrics.RecordSuccess();
        metrics.RecordFailure();
        metrics.RecordSkipped();
        metrics.Reset();
        Assert.Equal(0, metrics.DownloadSuccess);
        Assert.Equal(0, metrics.DownloadFailed);
        Assert.Equal(0, metrics.DownloadSkipped);
    }

    [Fact]
    public void Counters_Are_ThreadSafe()
    {
        var metrics = new DownloadMetrics();
        Parallel.For(0, 1000, _ => metrics.RecordSuccess());
        Assert.Equal(1000, metrics.DownloadSuccess);
    }
}
