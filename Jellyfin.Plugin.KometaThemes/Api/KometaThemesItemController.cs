using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;

#pragma warning disable CA2007, CA3003, SA1611, SA1615, CS1591

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for per-item theme management.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Items")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesItemController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly AnimeThemesDownloader _downloader;
    private readonly DownloadTracker _downloadTracker;
    private readonly ThemeLinkRepairService _linkRepair;
    private readonly FailedItemsStore _failedItems;
    private readonly ILogger<KometaThemesItemController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesItemController"/> class.
    /// </summary>
    public KometaThemesItemController(
        ILibraryManager libraryManager,
        AnimeThemesDownloader downloader,
        DownloadTracker downloadTracker,
        ThemeLinkRepairService linkRepair,
        FailedItemsStore failedItems,
        ILogger<KometaThemesItemController> logger)
    {
        _libraryManager = libraryManager;
        _downloader = downloader;
        _downloadTracker = downloadTracker;
        _linkRepair = linkRepair;
        _failedItems = failedItems;
        _logger = logger;
    }

    /// <summary>
    /// Repairs the linking between the item and its theme files (Jellyfin 10.11.x workaround).
    /// </summary>
    [HttpPost("{itemId}/repair")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RepairItemThemeLinks([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        try
        {
            var result = await _linkRepair.RepairAsync(item, CancellationToken.None);
            return Ok(new
            {
                songsOnDisk = result.SongsOnDisk,
                videosOnDisk = result.VideosOnDisk,
                repaired = result.Repaired,
                notScanned = result.NotScanned,
                registeredSongs = result.RegisteredSongs,
                registeredVideos = result.RegisteredVideos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error repairing theme links for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the manual binding for a specific item, if any.
    /// </summary>
    [HttpGet("{itemId}/binding")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetItemBinding([FromRoute, Required] Guid itemId)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return NotFound(new { error = "Plugin configuration not available" });
        }

        var itemIdString = itemId.ToString();
        var binding = configuration.ManualBindings
            .FirstOrDefault(b => string.Equals(b.ItemId, itemIdString, StringComparison.OrdinalIgnoreCase));

        if (binding == null)
        {
            return Ok(new { hasBinding = false });
        }

        return Ok(new
        {
            hasBinding = true,
            binding.AnimeId,
            binding.AnimeName,
            binding.Slug,
            binding.BoundAt,
            binding.Source
        });
    }

    /// <summary>
    /// Triggers a theme sync for a specific item. With <paramref name="force"/> the
    /// already-satisfied check is bypassed (used by the Unresolved tab retry button).
    /// </summary>
    [HttpPost("{itemId}/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SyncItemThemes([FromRoute, Required] Guid itemId, [FromQuery] bool force = false)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var originalForceSync = configuration.ForceSync;

        if (!force && !_downloader.ShouldUpdate(item, configuration) && !configuration.ForceSync)
        {
            return Ok(new { message = "Item already has themes and ForceSync is off", downloaded = false });
        }

        if (force)
        {
            configuration.ForceSync = true;
        }

        try
        {
            var resolvedItems = new List<ItemWithAnime>();
            await foreach (var resolved in _downloader.ResolveItems(new[] { item }, configuration, CancellationToken.None))
            {
                resolvedItems.Add(resolved);
            }

            foreach (var resolved in resolvedItems)
            {
                foreach (var anime in resolved.Anime)
                {
                    await _downloader.HandleAsync(resolved.Item, anime, configuration, CancellationToken.None);
                }
            }

            if (resolvedItems.Count > 0)
            {
                _failedItems.Remove(item.Id);
            }

            return Ok(new { message = "Sync completed successfully", downloaded = resolvedItems.Count > 0 });
        }
        catch (Exception ex)
        {
            _failedItems.Record(item, FailedItemReason.DownloadFailed, ex.Message);
            _logger.LogError(ex, "Error syncing themes for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
        finally
        {
            configuration.ForceSync = originalForceSync;
        }
    }

    /// <summary>
    /// Resolves themes for an item without downloading anything. Useful for preview/dry-run.
    /// </summary>
    [HttpPost("{itemId}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> PreviewItemThemes([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var originalForceSync = configuration.ForceSync;
        configuration.ForceSync = true;

        try
        {
            var resolvedItems = new List<ItemWithAnime>();
            await foreach (var resolved in _downloader.ResolveItems(new[] { item }, configuration, CancellationToken.None))
            {
                resolvedItems.Add(resolved);
            }

            var summary = new List<object>();
            foreach (var resolved in resolvedItems)
            {
                foreach (var anime in resolved.Anime)
                {
                    var themeCount = anime.Themes?.Count ?? 0;
                    var videoCount = anime.Themes?
                        .SelectMany(t => t.Entries ?? new System.Collections.ObjectModel.Collection<AnimeThemeEntry>())
                        .Sum(e => e.Videos?.Count ?? 0) ?? 0;
                    summary.Add(new
                    {
                        animeId = anime.Id,
                        animeName = anime.Name,
                        animeSlug = anime.Slug,
                        themes = themeCount,
                        videos = videoCount
                    });
                }
            }

            return Ok(new { preview = true, candidates = summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing themes for item {Id}", itemId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
        finally
        {
            configuration.ForceSync = originalForceSync;
        }
    }

    /// <summary>
    /// Gets the list of downloaded themes for an item.
    /// </summary>
    [HttpGet("{itemId}/themes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetItemThemes([FromRoute, Required] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var records = await _downloadTracker.LoadAsync(item.ContainingFolderPath);
        var result = records.Select(r => new
        {
            r.ThemeId,
            Type = r.Type.ToString(),
            r.Sequence,
            r.Slug,
            r.FileName,
            r.SeasonNumber,
            r.DownloadedAt,
            Exists = System.IO.File.Exists(Path.Combine(item.ContainingFolderPath, "theme-music", r.FileName)) ||
                     System.IO.File.Exists(Path.Combine(item.ContainingFolderPath, "backdrops", r.FileName))
        });

        return Ok(result);
    }

    /// <summary>
    /// Deletes downloaded themes for an item. When <paramref name="fileName"/> is provided,
    /// only that file is removed; otherwise all tracked themes are deleted.
    /// </summary>
    [HttpDelete("{itemId}/themes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteItemThemes([FromRoute, Required] Guid itemId, [FromQuery] string? fileName = null)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var records = (await _downloadTracker.LoadAsync(item.ContainingFolderPath)).ToList();

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // Guard against path traversal: only bare file names tracked by the plugin are accepted.
            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            {
                return BadRequest(new { error = "Invalid file name" });
            }

            var deletedSingle = DeleteThemeFile(item, fileName);
            var remaining = records.Where(r => !string.Equals(r.FileName, fileName, StringComparison.OrdinalIgnoreCase)).ToList();
            await _downloadTracker.SaveAsync(item.ContainingFolderPath, remaining);
            return Ok(new { message = $"Deleted {deletedSingle} theme files", deleted = deletedSingle });
        }

        var deleted = records.Sum(record => DeleteThemeFile(item, record.FileName));
        await _downloadTracker.SaveAsync(item.ContainingFolderPath, new List<DownloadRecord>());

        return Ok(new { message = $"Deleted {deleted} theme files", deleted });
    }

    private static int DeleteThemeFile(BaseItem item, string fileName)
    {
        var deleted = 0;
        foreach (var directory in new[] { "theme-music", "backdrops" })
        {
            var path = Path.Combine(item.ContainingFolderPath, directory, fileName);
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                deleted++;
            }
        }

        return deleted;
    }
}
