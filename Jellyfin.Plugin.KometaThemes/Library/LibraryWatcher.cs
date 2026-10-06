using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Library;

/// <summary>
/// Processes anime as they enter the library and cleans up after removed ones.
/// </summary>
/// <remarks>
/// <para>
/// Jellyfin raises <c>ItemAdded</c> as soon as a folder is found, before its metadata (and so its
/// AniList or AniDB ID) is downloaded. 1.x processed new items 30 seconds later, often before the IDs
/// existed, and fell back to a title search or gave up. Here a new item waits two minutes, and the
/// first metadata update of an item that was never processed queues it again.
/// </para>
/// <para>
/// 1.x could also lose items: one queued while a batch was running stayed queued until the next
/// library event. The queue is now polled on a timer.
/// </para>
/// </remarks>
public sealed class LibraryWatcher : IHostedService, IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Recently = TimeSpan.FromMinutes(10);

    private readonly ILibraryManager _libraryManager;
    private readonly LibraryScope _scope;
    private readonly ItemProcessor _processor;
    private readonly ThemeInstaller _installer;
    private readonly ILogger<LibraryWatcher> _logger;
    private readonly ConcurrentDictionary<Guid, (DateTime Due, DateTime First)> _pending = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _recent = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Timer? _timer;
    private int _working;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="scope">Library scope.</param>
    /// <param name="processor">Item processor.</param>
    /// <param name="installer">Theme installer, for cleanup.</param>
    /// <param name="logger">Logger.</param>
    public LibraryWatcher(ILibraryManager libraryManager, LibraryScope scope, ItemProcessor processor, ThemeInstaller installer, ILogger<LibraryWatcher> logger)
    {
        _libraryManager = libraryManager;
        _scope = scope;
        _processor = processor;
        _installer = installer;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        await _shutdown.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _shutdown.Dispose();
    }

    /// <summary>
    /// Marks an item as just processed, so the refresh that follows does not queue it again.
    /// </summary>
    /// <param name="itemId">Item ID.</param>
    public void MarkProcessed(Guid itemId) => _recent[itemId] = DateTime.UtcNow;

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is Series or Movie or Season or Episode)
        {
            Enqueue(LibraryScope.OwnerOf(e.Item), "added");
        }
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not (Series or Movie)
            || (e.UpdateReason & (ItemUpdateType.MetadataDownload | ItemUpdateType.MetadataImport)) == 0
            || _pending.ContainsKey(e.Item.Id)
            || (_recent.TryGetValue(e.Item.Id, out var at) && DateTime.UtcNow - at < Recently))
        {
            return;
        }

        // Only items never processed: everything else is the full check's job.
        var folder = e.Item.IsFolder ? e.Item.Path : e.Item.ContainingFolderPath;
        if (!string.IsNullOrEmpty(folder) && !File.Exists(FolderStateStore.PathOf(folder)))
        {
            Enqueue(e.Item, "metadata ready");
        }
    }

    private void Enqueue(BaseItem? owner, string why)
    {
        if (owner == null || !Config.AutoSyncOnItemAdded || Config.FindSkipped(owner.Id) != null)
        {
            return;
        }

        try
        {
            if (!_scope.IsInScope(owner, Config))
            {
                return;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogDebug(ex, "Could not tell the library of {Name}", owner.Name);
            return;
        }

        var now = DateTime.UtcNow;
        _pending.AddOrUpdate(
            owner.Id,
            _ => (now + Settle, now),
            (_, existing) => (Min(now + Settle, existing.First + MaxWait), existing.First));
        _logger.LogDebug("{Name} queued for themes ({Why})", owner.Name, why);
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private void Tick()
    {
        if (_shutdown.IsCancellationRequested || Interlocked.CompareExchange(ref _working, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var id in _pending.Where(p => p.Value.Due <= now).Select(p => p.Key).ToList())
                {
                    _shutdown.Token.ThrowIfCancellationRequested();
                    _pending.TryRemove(id, out _);
                    if (_libraryManager.GetItemById(id) is not { } owner)
                    {
                        continue;
                    }

                    MarkProcessed(id);
                    await _processor.ProcessAsync(owner, Config, false, _shutdown.Token).ConfigureAwait(false);
                    MarkProcessed(id);
                }

                foreach (var old in _recent.Where(r => now - r.Value > Recently).Select(r => r.Key).ToList())
                {
                    _recent.TryRemove(old, out _);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not process newly added anime");
            }
            finally
            {
                Volatile.Write(ref _working, 0);
            }
        });
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (!Config.CleanupThemesOnItemRemoved || e.Item is not (Series or Movie or Season))
        {
            return;
        }

        var item = e.Item;
        var folder = item.IsFolder ? item.Path : item.ContainingFolderPath;
        if (string.IsNullOrEmpty(folder) || !File.Exists(FolderStateStore.PathOf(folder)))
        {
            return;
        }

        _pending.TryRemove(item.Id, out _);
        _ = Task.Run(async () =>
        {
            try
            {
                // A movie can share its folder with others: remove only its own files.
                var deleted = await _installer.DeleteAllAsync(folder, item is Movie ? item.Id : null, _shutdown.Token).ConfigureAwait(false);
                if (deleted > 0)
                {
                    _logger.LogInformation("Removed {Count} theme file(s) of {Name}, which left the library", deleted, item.Name);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove the themes of {Name}", item.Name);
            }
        });
    }
}
