using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Library;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Runs the full check: every managed series and movie, one at a time.
/// </summary>
public sealed class SyncRunner : IDisposable
{
    private const int ResolveBatch = 50;

    private readonly LibraryScope _scope;
    private readonly AnimeResolver _resolver;
    private readonly ItemProcessor _processor;
    private readonly FailedItemsStore _problems;
    private readonly ActivityLog _activity;
    private readonly LibraryIndex _index;
    private readonly ILogger<SyncRunner> _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private SyncStatus _status = SyncStatus.Idle;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncRunner"/> class.
    /// </summary>
    /// <param name="scope">Library scope.</param>
    /// <param name="resolver">Anime resolver.</param>
    /// <param name="processor">Item processor.</param>
    /// <param name="problems">Problem store.</param>
    /// <param name="activity">Activity list.</param>
    /// <param name="index">Library index.</param>
    /// <param name="logger">Logger.</param>
    public SyncRunner(
        LibraryScope scope,
        AnimeResolver resolver,
        ItemProcessor processor,
        FailedItemsStore problems,
        ActivityLog activity,
        LibraryIndex index,
        ILogger<SyncRunner> logger)
    {
        _scope = scope;
        _resolver = resolver;
        _processor = processor;
        _problems = problems;
        _activity = activity;
        _index = index;
        _logger = logger;
    }

    /// <summary>Gets the current status.</summary>
    public SyncStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>
    /// Starts a check in the background.
    /// </summary>
    /// <param name="retryProblems">Whether items waiting after a failure are tried now as well.</param>
    /// <returns>False when a check is already running.</returns>
    public bool TryStart(bool retryProblems)
    {
        lock (_gate)
        {
            if (_current != null)
            {
                return false;
            }

            _current = new CancellationTokenSource();
        }

        var token = _current.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunOwnedAsync("manual", retryProblems, null, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("KometaThemes check cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KometaThemes check failed");
            }
        });

        return true;
    }

    /// <summary>
    /// Runs a check and waits for it; used by the scheduled task.
    /// </summary>
    /// <param name="progress">Progress for Jellyfin's task list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RunScheduledAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            if (_current != null)
            {
                _logger.LogInformation("A KometaThemes check is already running; the scheduled one is skipped");
                return;
            }

            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _current = linked;
        }

        await RunOwnedAsync("schedule", false, progress, linked.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the running check to stop after the current anime.
    /// </summary>
    /// <returns>False when no check is running.</returns>
    public bool Cancel()
    {
        lock (_gate)
        {
            if (_current == null)
            {
                return false;
            }

            _current.Cancel();
            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _current?.Cancel();
            _current?.Dispose();
            _current = null;
        }
    }

    private async Task RunOwnedAsync(string trigger, bool retryProblems, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var downloaded = 0;
        var problems = 0;
        var done = 0;
        var message = string.Empty;
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (retryProblems)
            {
                _problems.ResetSchedules();
            }

            var skipped = config.GetSkippedIds();
            var now = DateTime.UtcNow;
            var items = _scope.GetItems(config)
                .Where(item => !skipped.Contains(item.Id) && _problems.IsDue(item, now))
                .ToList();

            SetStatus(new SyncStatus(true, "matching", trigger, items.Count, 0, 0, 0, null, started, null, null));
            _logger.LogInformation("KometaThemes check started ({Trigger}): {Count} anime", trigger, items.Count);

            foreach (var batch in items.Chunk(ResolveBatch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetStatus(Status with { Phase = "matching", Current = null });
                var resolutions = await _resolver.ResolveAsync(batch, config, cancellationToken).ConfigureAwait(false);

                foreach (var item in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetStatus(Status with { Phase = "downloading", Current = item.Name });
                    try
                    {
                        var report = await _processor.ProcessAsync(item, resolutions[item.Id], config, false, cancellationToken).ConfigureAwait(false);
                        downloaded += report.Downloaded;
                        if (report.Failed > 0 || report.Resolution.State is MatchState.NotFound)
                        {
                            problems++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        problems++;
                        _logger.LogError(ex, "Could not process {Name}", item.Name);
                        _problems.Record(item, ProblemKind.DownloadFailed, ex.Message);
                    }

                    done++;
                    progress?.Report(items.Count == 0 ? 100 : 100.0 * done / items.Count);
                    SetStatus(Status with { Done = done, Downloaded = downloaded, Failed = problems });
                }
            }

            message = string.Create(
                CultureInfo.InvariantCulture,
                $"Checked {done} anime: {downloaded} file(s) downloaded, {problems} need attention");
            _activity.Add(ActivityKind.Checked, null, null, message);
            Plugin.MutateConfiguration(c =>
            {
                c.LastFullSyncUtc = DateTime.UtcNow;
                c.LastSyncSummary = message;
            });
        }
        catch (OperationCanceledException)
        {
            message = string.Create(CultureInfo.InvariantCulture, $"Stopped after {done} anime");
            throw;
        }
        catch (Exception ex)
        {
            message = "The check stopped: " + ex.Message;
            throw;
        }
        finally
        {
            _index.Invalidate();
            lock (_gate)
            {
                _status = _status with { Running = false, Phase = "idle", Current = null, FinishedUtc = DateTime.UtcNow, Message = message };
                _current?.Dispose();
                _current = null;
            }

            progress?.Report(100);
        }
    }

    private void SetStatus(SyncStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }
    }
}
