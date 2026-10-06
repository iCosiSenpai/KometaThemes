using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.KometaThemes.Library;

/// <summary>
/// One row of the library list.
/// </summary>
/// <param name="Id">Item ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Year">Year.</param>
/// <param name="Type"><c>Series</c> or <c>Movie</c>.</param>
/// <param name="Seasons">Seasons in the library.</param>
/// <param name="SeasonsWithOwnThemes">Seasons with themes of their own.</param>
/// <param name="Openings">Distinct openings on disk.</param>
/// <param name="Endings">Distinct endings on disk.</param>
/// <param name="Status">One of <c>ready</c>, <c>empty</c>, <c>pending</c>, <c>attention</c>, <c>failed</c>, <c>excluded</c>.</param>
/// <param name="Detail">Explanation for problems.</param>
/// <param name="Manual">Whether the owner chose the match.</param>
/// <param name="CheckedUtc">When it was last brought up to date.</param>
public sealed record ItemSummary(
    Guid Id,
    string Name,
    int? Year,
    string Type,
    int Seasons,
    int SeasonsWithOwnThemes,
    int Openings,
    int Endings,
    string Status,
    string? Detail,
    bool Manual,
    DateTime? CheckedUtc);

/// <summary>
/// Summary of every managed item, computed from the state files and kept for a minute.
/// </summary>
public sealed class LibraryIndex : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ILibraryManager _libraryManager;
    private readonly LibraryScope _scope;
    private readonly FolderStateStore _store;
    private readonly FailedItemsStore _problems;
    private readonly ConcurrentDictionary<Guid, ItemSummary> _cache = new();
    private readonly SemaphoreSlim _rebuild = new(1, 1);
    private DateTime _builtUtc = DateTime.MinValue;
    private IReadOnlyList<Guid> _order = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryIndex"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="scope">Library scope.</param>
    /// <param name="store">State file store.</param>
    /// <param name="problems">Problem store.</param>
    public LibraryIndex(ILibraryManager libraryManager, LibraryScope scope, FolderStateStore store, FailedItemsStore problems)
    {
        _libraryManager = libraryManager;
        _scope = scope;
        _store = store;
        _problems = problems;
    }

    /// <summary>
    /// Gets the summaries of every managed item.
    /// </summary>
    /// <param name="config">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summaries in library order.</returns>
    public async Task<IReadOnlyList<ItemSummary>> GetAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        await _rebuild.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow - _builtUtc > Lifetime)
            {
                var items = _scope.GetItems(config);
                _cache.Clear();
                foreach (var item in items)
                {
                    _cache[item.Id] = await SummarizeAsync(item, config, cancellationToken).ConfigureAwait(false);
                }

                _order = items.Select(item => item.Id).ToList();
                _builtUtc = DateTime.UtcNow;
            }
            else
            {
                foreach (var id in _order.Where(id => !_cache.ContainsKey(id)).ToList())
                {
                    if (_libraryManager.GetItemById(id) is { } item)
                    {
                        _cache[id] = await SummarizeAsync(item, config, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            return _order.Select(id => _cache.TryGetValue(id, out var summary) ? summary : null).OfType<ItemSummary>().ToList();
        }
        finally
        {
            _rebuild.Release();
        }
    }

    /// <summary>
    /// Drops one item's summary, or all of them, so the next read recomputes it.
    /// </summary>
    /// <param name="itemId">Item ID, or null for all.</param>
    public void Invalidate(Guid? itemId = null)
    {
        if (itemId is { } id)
        {
            _cache.TryRemove(id, out _);
        }
        else
        {
            _builtUtc = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Computes one item's summary.
    /// </summary>
    /// <param name="item">Series or movie.</param>
    /// <param name="config">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary.</returns>
    public async Task<ItemSummary> SummarizeAsync(BaseItem item, PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);

        var folder = item.IsFolder ? item.Path : item.ContainingFolderPath;
        var states = new List<(bool Season, FolderState State)>();
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            states.Add((false, await _store.LoadAsync(folder, cancellationToken).ConfigureAwait(false)));
            if (item.IsFolder)
            {
                foreach (var sub in SafeSubfolders(folder))
                {
                    if (File.Exists(FolderStateStore.PathOf(sub)))
                    {
                        states.Add((true, await _store.LoadAsync(sub, cancellationToken).ConfigureAwait(false)));
                    }
                }
            }
        }

        // Movies sharing a folder: only count the records of this movie.
        var records = states.SelectMany(s => s.State.Records.Where(r => r.ItemId == Guid.Empty || r.ItemId == item.Id || s.Season)).ToList();
        var openings = records.Where(r => r.Type == ThemeType.OP).Select(r => (r.AnimeId, r.ThemeId)).Distinct().Count();
        var endings = records.Where(r => r.Type == ThemeType.ED).Select(r => (r.AnimeId, r.ThemeId)).Distinct().Count();
        var seasonsWithThemes = states.Count(s => s.Season && s.State.Records.Count > 0);
        var checkedUtc = states.Select(s => s.State.CheckedUtc).Where(d => d.HasValue).DefaultIfEmpty().Max();
        var seasons = item.IsFolder
            ? _libraryManager.GetCount(new InternalItemsQuery { ParentId = item.Id, IncludeItemTypes = [BaseItemKind.Season], IsVirtualItem = false })
            : 0;

        var problem = _problems.Get(item.Id);
        string status;
        string? detail = null;
        if (config.FindSkipped(item.Id) != null)
        {
            status = "excluded";
        }
        else if (problem?.Reason == ProblemKind.Unresolved)
        {
            status = "attention";
            detail = problem.Error;
        }
        else if (problem?.Reason == ProblemKind.DownloadFailed)
        {
            status = "failed";
            detail = problem.Error;
        }
        else if (records.Count > 0)
        {
            status = "ready";
        }
        else if (checkedUtc != null)
        {
            status = "empty";
            detail = "No theme on animethemes.moe fits your settings.";
        }
        else
        {
            status = "pending";
        }

        return new ItemSummary(
            item.Id,
            item.Name ?? string.Empty,
            item.ProductionYear,
            item.GetBaseItemKind().ToString(),
            seasons,
            seasonsWithThemes,
            openings,
            endings,
            status,
            detail,
            config.FindBinding(item.Id) != null,
            checkedUtc);
    }

    /// <inheritdoc />
    public void Dispose() => _rebuild.Dispose();

    private static List<string> SafeSubfolders(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .Where(path => !string.Equals(Path.GetFileName(path), ThemeFileKinds.AudioDirectory, StringComparison.Ordinal)
                    && !string.Equals(Path.GetFileName(path), ThemeFileKinds.VideoDirectory, StringComparison.Ordinal))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
