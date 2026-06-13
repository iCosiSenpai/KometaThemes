using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Model.Tasks;

#pragma warning disable SA1611, CS1591

namespace Jellyfin.Plugin.KometaThemes.Tasks;

/// <summary>
/// Scheduled task that syncs anime themes from animethemes.moe.
/// Supports multi-season and per-item processing.
/// </summary>
public class SyncThemesTask : IScheduledTask
{
    private readonly SyncThemesRunner _runner;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncThemesTask"/> class.
    /// </summary>
    public SyncThemesTask(
        SyncThemesRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name => "KometaThemes: Sync Anime Themes";

    /// <inheritdoc />
    public string Key => "KometaThemesSyncThemes";

    /// <inheritdoc />
    public string Description => "Downloads anime theme songs and videos from animethemes.moe with multi-provider resolution and multi-season support.";

    /// <inheritdoc />
    public string Category => "KometaThemes";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var intervalHours = Math.Clamp(configuration.SyncIntervalHours, 1, 168);

        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(intervalHours).Ticks
            }
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await _runner.RunScheduledAsync(progress, cancellationToken).ConfigureAwait(false);
    }
}
