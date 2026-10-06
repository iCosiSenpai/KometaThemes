using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Library;

/// <summary>
/// A Jellyfin library as the settings page shows it.
/// </summary>
/// <param name="Id">Library ID.</param>
/// <param name="Name">Library name.</param>
/// <param name="CollectionType">Content type, such as <c>tvshows</c>.</param>
/// <param name="Selected">Whether KometaThemes manages it.</param>
public sealed record LibraryInfo(Guid Id, string Name, string? CollectionType, bool Selected);

/// <summary>
/// Decides which libraries and items KometaThemes manages.
/// </summary>
public sealed class LibraryScope
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryScope> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryScope"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="logger">Logger.</param>
    public LibraryScope(ILibraryManager libraryManager, ILogger<LibraryScope> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Lists every library with whether it is managed.
    /// </summary>
    /// <param name="config">Settings.</param>
    /// <returns>Libraries, sorted by name.</returns>
    public IReadOnlyList<LibraryInfo> GetLibraries(PluginConfiguration config)
    {
        var selected = SelectedIds(config);
        return _libraryManager.GetVirtualFolders()
            .Select(folder => (Folder: folder, Id: Guid.TryParse(folder.ItemId, out var id) ? id : Guid.Empty))
            .Where(pair => pair.Id != Guid.Empty)
            .Select(pair => new LibraryInfo(pair.Id, pair.Folder.Name, pair.Folder.CollectionType?.ToString(), selected.Contains(pair.Id)))
            .OrderBy(library => library.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets the managed library IDs. On the first call after an upgrade, turns the 1.x name pattern
    /// into an explicit list and saves it.
    /// </summary>
    /// <param name="config">Settings.</param>
    /// <returns>Library IDs.</returns>
    public HashSet<Guid> SelectedIds(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.LibraryIds.Count == 0 && config.LibraryPattern != null)
        {
            var matches = LibraryPatternMatcher.Create(config.LibraryPattern);
            var ids = _libraryManager.GetVirtualFolders()
                .Where(folder => matches(folder.Name))
                .Select(folder => folder.ItemId)
                .Where(id => Guid.TryParse(id, out _))
                .ToList();

            _logger.LogInformation("Libraries matching the 1.x pattern {Pattern}: {Count}; saved as the managed libraries", config.LibraryPattern, ids.Count);
            Plugin.MutateConfiguration(c =>
            {
                c.LibraryIds.Clear();
                foreach (var id in ids)
                {
                    c.LibraryIds.Add(id);
                }

                c.LibraryPattern = null;
            });

            return ids.Select(Guid.Parse).ToHashSet();
        }

        return config.LibraryIds
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    /// <summary>
    /// Gets every series and movie in the managed libraries, excluded items included.
    /// </summary>
    /// <param name="config">Settings.</param>
    /// <returns>Items sorted by name.</returns>
    public IReadOnlyList<BaseItem> GetItems(PluginConfiguration config)
    {
        var libraries = SelectedIds(config);
        if (libraries.Count == 0)
        {
            return [];
        }

        return _libraryManager.GetItemList(new InternalItemsQuery
            {
                AncestorIds = libraries.ToArray(),
                IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Movie],
                IsVirtualItem = false,
                Recursive = true,
            })
            .OrderBy(item => item.SortName ?? item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Tells whether an item lives in a managed library.
    /// </summary>
    /// <param name="item">Series, season, episode or movie.</param>
    /// <param name="config">Settings.</param>
    /// <returns>True when managed.</returns>
    public bool IsInScope(BaseItem item, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(item);
        var selected = SelectedIds(config);
        return selected.Count > 0 && _libraryManager.GetCollectionFolders(item).Any(folder => selected.Contains(folder.Id));
    }

    /// <summary>
    /// Gets the series or movie an item belongs to.
    /// </summary>
    /// <param name="item">Any item.</param>
    /// <returns>The series or movie, or null.</returns>
    public static BaseItem? OwnerOf(BaseItem? item) => item switch
    {
        Series or MediaBrowser.Controller.Entities.Movies.Movie => item,
        Season season => season.Series,
        Episode episode => episode.Series,
        _ => null
    };
}
