using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Reacts to library item additions and triggers theme sync for new anime.
/// Constructed eagerly via <see cref="Plugin"/> to subscribe to <see cref="ILibraryManager.ItemAdded"/>.
/// </summary>
public sealed class LibrarySyncHandler : IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly SyncThemesRunner _runner;
    private readonly ILogger<LibrarySyncHandler> _logger;
    private readonly object _batchLock = new();
    private readonly HashSet<Guid> _pendingItems = new();
    private readonly SemaphoreSlim _runGate = new(2, 2);
    private readonly ItemRemovedHandler _itemRemovedHandler;
    private Timer? _debounceTimer;
    private bool _disposed;

    public LibrarySyncHandler(
        ILibraryManager libraryManager,
        SyncThemesRunner runner,
        ILogger<LibrarySyncHandler> logger,
        ItemRemovedHandler itemRemovedHandler)
    {
        _libraryManager = libraryManager;
        _runner = runner;
        _logger = logger;
        _itemRemovedHandler = itemRemovedHandler;

        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _logger.LogInformation("LibrarySyncHandler started — watching for new items");
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        if (_disposed || e.Item == null)
        {
            return;
        }

        var item = e.Item;
        if (item.GetBaseItemKind() != BaseItemKind.Series && item.GetBaseItemKind() != BaseItemKind.Movie)
        {
            return;
        }

        _logger.LogInformation("Item removed: {Name} ({Id}) — checking cleanup policy", item.Name, item.Id);
        _itemRemovedHandler.HandleRemoved(item.Id, item.ContainingFolderPath);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (Plugin.Instance?.Configuration?.AutoSyncOnItemAdded == false)
        {
            return;
        }

        var item = e.Item;
        if (item == null)
        {
            return;
        }

        var kind = item.GetBaseItemKind();
        if (kind != BaseItemKind.Series && kind != BaseItemKind.Movie)
        {
            return;
        }

        _logger.LogInformation("New {Type} detected: {Name} ({Id}) — queuing for theme sync", kind, item.Name, item.Id);

        lock (_batchLock)
        {
            _pendingItems.Add(item.Id);
        }

        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(
            async _ => await FlushPendingItems().ConfigureAwait(false),
            null,
            TimeSpan.FromSeconds(30),
            Timeout.InfiniteTimeSpan);
    }

    private async Task FlushPendingItems()
    {
        Guid[] batch;
        lock (_batchLock)
        {
            batch = _pendingItems.ToArray();
            _pendingItems.Clear();
        }

        if (batch.Length == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} newly added items for theme sync", batch.Length);

        foreach (var itemId in batch)
        {
            try
            {
                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    _logger.LogWarning("Item {Id} no longer exists, skipping", itemId);
                    continue;
                }

                await _runGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await _runner.SyncItemAsync(item, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _runGate.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync themes for new item {Id}", itemId);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _runGate.Dispose();
        _logger.LogInformation("LibrarySyncHandler disposed");
    }
}
