using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Shared helpers for selecting Jellyfin library items from plugin include/exclude settings.
/// </summary>
internal static class LibrarySelection
{
    /// <summary>
    /// Gets all series and movies from libraries matched by the plugin configuration.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>Eligible Jellyfin library items.</returns>
    public static IEnumerable<BaseItem> GetEligibleItems(
        ILibraryManager libraryManager,
        PluginConfiguration configuration)
    {
        var pattern = string.IsNullOrWhiteSpace(configuration.LibraryPattern)
            ? "Anime"
            : configuration.LibraryPattern;
        var includeRegex = new Regex(pattern, RegexOptions.IgnoreCase);

        var libraryIds = libraryManager.GetVirtualFolders()
            .Where(lib => includeRegex.IsMatch(lib.Name))
            .Select(lib => Guid.TryParse(lib.ItemId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToArray();

        var skipped = configuration.GetSkippedItemsDictionary();

        return libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = libraryIds,
            IncludeItemTypes = new[] { BaseItemKind.Series, BaseItemKind.Movie },
            IsVirtualItem = false,
            Recursive = true
        }).Where(item => !skipped.ContainsKey(item.Id.ToString()));
    }

    /// <summary>
    /// Determines if a specific item is eligible for KometaThemes UI and processing.
    /// Checks if the item belongs to a library matching the LibraryPattern and is not skipped.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="configuration">Plugin configuration.</param>
    /// <returns>True if eligible.</returns>
    public static bool IsItemEligible(
        BaseItem item,
        ILibraryManager libraryManager,
        PluginConfiguration configuration)
    {
        if (item == null)
        {
            return false;
        }

        if (item.GetBaseItemKind() != BaseItemKind.Series &&
            item.GetBaseItemKind() != BaseItemKind.Movie)
        {
            return false;
        }

        var pattern = string.IsNullOrWhiteSpace(configuration.LibraryPattern)
            ? "Anime"
            : configuration.LibraryPattern;
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        // Walk up the parent chain to find the CollectionFolder (library)
        var current = item;
        while (current != null)
        {
            if (current is CollectionFolder folder && regex.IsMatch(folder.Name))
            {
                var skipped = configuration.GetSkippedItemsDictionary();
                if (!skipped.ContainsKey(item.Id.ToString()))
                {
                    return true;
                }
            }

            current = current.GetParent();
        }

        return false;
    }
}
