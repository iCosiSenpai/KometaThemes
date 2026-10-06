using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Library;

/// <summary>
/// Tells Jellyfin to pick up theme files that were added, renamed or removed.
/// </summary>
/// <remarks>
/// 1.x ran a full refresh with "replace all metadata" after every download: every metadata provider
/// was queried again and non-locked fields could be overwritten, all to register one theme song. In
/// Jellyfin 12 a refresh always re-reads an item's extras, so a default refresh does it, exactly as a
/// library scan would. Images are only validated, never fetched.
/// </remarks>
public sealed class ItemRefresher
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<ItemRefresher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemRefresher"/> class.
    /// </summary>
    /// <param name="fileSystem">Jellyfin file system.</param>
    /// <param name="logger">Logger.</param>
    public ItemRefresher(IFileSystem fileSystem, ILogger<ItemRefresher> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes an item's extras.
    /// </summary>
    /// <param name="item">Series, season or movie.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RefreshAsync(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.Default,
            ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
            ReplaceAllMetadata = false,
            ReplaceAllImages = false,
            ForceSave = false,
        };

        try
        {
            await item.RefreshMetadata(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The files are in place either way; the next library scan registers them.
            _logger.LogWarning(ex, "Jellyfin could not refresh {Name}; its themes appear after the next library scan", item.Name);
        }
    }
}
