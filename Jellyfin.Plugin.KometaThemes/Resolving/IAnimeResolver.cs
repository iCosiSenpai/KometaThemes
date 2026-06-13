using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Interface for resolving Jellyfin library items to AnimeThemes anime objects.
/// </summary>
public interface IAnimeResolver
{
    /// <summary>
    /// Resolves a batch of library items to their corresponding anime objects.
    /// </summary>
    /// <param name="items">Items to resolve.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of resolved items with their anime.</returns>
    IAsyncEnumerable<ItemWithAnime> ResolveItemsAsync(
        BaseItem[] items,
        PluginConfiguration config,
        CancellationToken cancellationToken);
}
