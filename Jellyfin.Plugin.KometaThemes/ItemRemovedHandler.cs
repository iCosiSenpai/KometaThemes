using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
#pragma warning disable SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Reacts to library item removals and optionally cleans up orphaned theme files.
/// Controlled by <see cref="Configuration.PluginConfiguration.CleanupThemesOnItemRemoved"/>.
/// </summary>
public sealed class ItemRemovedHandler : IDisposable
{
    private readonly ILogger<ItemRemovedHandler> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemRemovedHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ItemRemovedHandler(ILogger<ItemRemovedHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles a removed library item by cleaning its theme directories and tracker file when configured.
    /// </summary>
    /// <param name="itemId">The removed item ID.</param>
    /// <param name="containingFolderPath">The folder the item lived in (may be null/empty).</param>
    public void HandleRemoved(Guid itemId, string? containingFolderPath)
    {
        if (_disposed)
        {
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config?.CleanupThemesOnItemRemoved != true)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(containingFolderPath))
        {
            return;
        }

        Task.Run(() => CleanupAsync(itemId, containingFolderPath));
    }

    private async Task CleanupAsync(Guid itemId, string containingFolderPath)
    {
        try
        {
            var themeMusic = Path.Combine(containingFolderPath, "theme-music");
            var backdrops = Path.Combine(containingFolderPath, "backdrops");
            var tracker = Path.Combine(containingFolderPath, "_kometa_themes.json");

            if (Directory.Exists(themeMusic))
            {
                Directory.Delete(themeMusic, recursive: true);
                _logger.LogInformation("[{Id}] Removed orphan theme-music directory", itemId);
            }

            if (Directory.Exists(backdrops))
            {
                Directory.Delete(backdrops, recursive: true);
                _logger.LogInformation("[{Id}] Removed orphan backdrops directory", itemId);
            }

            if (File.Exists(tracker))
            {
                File.Delete(tracker);
                _logger.LogInformation("[{Id}] Removed orphan tracker file", itemId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{Id}] Failed to cleanup orphan theme files in {Path}", itemId, containingFolderPath);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }
}
