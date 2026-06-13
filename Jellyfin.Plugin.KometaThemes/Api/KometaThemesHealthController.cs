using System;
using System.Collections.Generic;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Lightweight operational health endpoint exposing plugin version, last sync state,
/// cache statistics and download metrics.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Health")]
public class KometaThemesHealthController : ControllerBase
{
    private readonly IResolutionCache _cache;
    private readonly SyncStatusTracker _statusTracker;
    private readonly DownloadMetrics _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesHealthController"/> class.
    /// </summary>
    /// <param name="cache">The shared resolution cache.</param>
    /// <param name="statusTracker">The live sync status tracker.</param>
    /// <param name="metrics">In-process download metrics counters.</param>
    public KometaThemesHealthController(
        IResolutionCache cache,
        SyncStatusTracker statusTracker,
        DownloadMetrics metrics)
    {
        _cache = cache;
        _statusTracker = statusTracker;
        _metrics = metrics;
    }

    /// <summary>
    /// Gets a health snapshot for the KometaThemes plugin.
    /// </summary>
    /// <returns>The current health snapshot.</returns>
    [HttpGet]
    public ActionResult Get()
    {
        var configuration = Plugin.Instance?.Configuration;
        var cacheStats = _cache.GetStats();
        var status = _statusTracker.GetStatus();

        return Ok(new
        {
            version = Plugin.Instance?.Version.ToString() ?? "unknown",
            isRunning = status.Phase != "idle" && !status.IsFinished,
            lastFullSyncUtc = configuration?.LastFullSyncUtc,
            lastSyncSummary = configuration?.LastSyncSummary ?? string.Empty,
            sync = new
            {
                status.Phase,
                status.TotalItems,
                status.ProcessedItems,
                status.ResolvedItems,
                status.DownloadedItems,
                status.SkippedItems,
                status.ProgressPercent
            },
            cache = new
            {
                totalEntries = cacheStats.PositiveEntries + cacheStats.NegativeEntries,
                positiveEntries = cacheStats.PositiveEntries,
                negativeEntries = cacheStats.NegativeEntries,
                hits = cacheStats.TotalHits,
                misses = cacheStats.TotalMisses
            },
            metrics = new Dictionary<string, object>
            {
                ["downloadsSuccess"] = _metrics.DownloadSuccess,
                ["downloadsFailed"] = _metrics.DownloadFailed,
                ["downloadsSkipped"] = _metrics.DownloadSkipped
            },
            rateLimitPerMinute = configuration?.RateLimitPerMinute ?? 0,
            autoSyncEnabled = configuration?.AutoSyncOnItemAdded ?? true,
            cleanupOnRemove = configuration?.CleanupThemesOnItemRemoved ?? false,
            updatedUtc = DateTime.UtcNow
        });
    }
}
