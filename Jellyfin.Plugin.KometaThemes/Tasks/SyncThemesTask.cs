using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.KometaThemes.Tasks;

/// <summary>
/// Scheduled check of every managed series and movie.
/// </summary>
/// <remarks>
/// The schedule is Jellyfin's to edit (Dashboard, Scheduled tasks). 1.x also had an interval setting
/// on its own page that only applied to a fresh install, because Jellyfin keeps the triggers it saved.
/// </remarks>
public sealed class SyncThemesTask : IScheduledTask
{
    private readonly SyncRunner _runner;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncThemesTask"/> class.
    /// </summary>
    /// <param name="runner">Check runner.</param>
    public SyncThemesTask(SyncRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public string Name => "Check anime themes";

    /// <inheritdoc />
    public string Key => "KometaThemesSyncThemes";

    /// <inheritdoc />
    public string Description => "Downloads missing openings and endings from animethemes.moe, renames and removes what changed.";

    /// <inheritdoc />
    public string Category => "KometaThemes";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        =>
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(12).Ticks
            }
        ];

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _runner.RunScheduledAsync(progress, cancellationToken);
}
