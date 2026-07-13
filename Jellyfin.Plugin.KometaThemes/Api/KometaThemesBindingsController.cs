using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.KometaThemes.Models;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, SA1615, CS1591, CA3003

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for managing manual item-to-anime bindings.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Bindings")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesBindingsController : ControllerBase
{
    private const string ThemeMusicDirectory = "theme-music";
    private const string ThemeVideoDirectory = "backdrops";
    private const string RootThemeSongFileName = "theme.mp3";

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<KometaThemesBindingsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesBindingsController"/> class.
    /// </summary>
    public KometaThemesBindingsController(
        ILibraryManager libraryManager,
        ILogger<KometaThemesBindingsController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Lists all manual bindings.
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<object>> GetBindings()
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration == null)
        {
            return Ok(Enumerable.Empty<object>());
        }

        var bindings = configuration.ManualBindings
            .OrderByDescending(b => b.BoundAt)
            .Select(b =>
            {
                var item = _libraryManager.GetItemById(Guid.Parse(b.ItemId));
                return new
                {
                    b.ItemId,
                    itemName = item?.Name ?? b.AnimeName,
                    itemType = item?.GetBaseItemKind().ToString(),
                    b.AnimeId,
                    b.AnimeName,
                    b.Slug,
                    b.BoundAt,
                    b.Source
                };
            });

        return Ok(bindings);
    }

    /// <summary>
    /// Creates or updates a manual binding for an item.
    /// </summary>
    [HttpPost("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SaveBinding(
        [FromRoute, Required] Guid itemId,
        [FromBody, Required] SaveBindingRequest request)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            return NotFound(new { error = "Item not found" });
        }

        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin not available" });
        }

        UpsertBinding(plugin, item, request.AnimeId, request.AnimeName, request.Slug, request.Source ?? "Manual");

        _logger.LogInformation(
            "Manual binding saved for item {ItemId} ({Name}) to anime {AnimeId}",
            itemId,
            item.Name,
            request.AnimeId);

        return Ok(new { message = $"'{item.Name}' bound to '{request.AnimeName}'", binding = request });
    }

    /// <summary>
    /// Removes a manual binding. Optionally deletes the downloaded theme files.
    /// </summary>
    [HttpDelete("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RemoveBinding(
        [FromRoute, Required] Guid itemId,
        [FromQuery] bool deleteFiles = false)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin not available" });
        }

        var item = _libraryManager.GetItemById(itemId);
        var itemIdString = itemId.ToString();
        var configuration = plugin.Configuration;
        var existing = configuration.ManualBindings
            .FirstOrDefault(b => string.Equals(b.ItemId, itemIdString, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            return NotFound(new { error = "Binding not found" });
        }

        configuration.ManualBindings.Remove(existing);
        plugin.SaveConfiguration();

        var deletedFiles = 0;
        if (deleteFiles && item != null && !string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            deletedFiles = DeleteThemeFiles(item);
        }

        _logger.LogInformation(
            "Manual binding removed for item {ItemId}; deleteFiles={DeleteFiles}, deleted={Deleted}",
            itemId,
            deleteFiles,
            deletedFiles);

        return Ok(new
        {
            message = $"Binding removed for '{item?.Name ?? existing.AnimeName}'",
            deletedFiles
        });
    }

    /// <summary>
    /// Unlocks an item by removing its manual binding so automatic resolution can take over again.
    /// Existing files are preserved.
    /// </summary>
    [HttpPost("{itemId}/unlock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult UnlockBinding([FromRoute, Required] Guid itemId)
    {
        var plugin = Plugin.Instance;
        if (plugin == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Plugin not available" });
        }

        var item = _libraryManager.GetItemById(itemId);
        var itemIdString = itemId.ToString();
        var configuration = plugin.Configuration;
        var existing = configuration.ManualBindings
            .FirstOrDefault(b => string.Equals(b.ItemId, itemIdString, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            return NotFound(new { error = "Binding not found" });
        }

        configuration.ManualBindings.Remove(existing);
        plugin.SaveConfiguration();

        _logger.LogInformation(
            "Manual binding unlocked for item {ItemId}; automatic resolution will apply on next sync",
            itemId);

        return Ok(new
        {
            message = $"Binding unlocked for '{item?.Name ?? existing.AnimeName}'. The next sync will try automatic resolution.",
            unlocked = true
        });
    }

    private static void UpsertBinding(
        Plugin plugin,
        BaseItem item,
        int animeId,
        string animeName,
        string slug,
        string source)
    {
        var configuration = plugin.Configuration;
        var itemId = item.Id.ToString();
        var existing = configuration.ManualBindings
            .FirstOrDefault(b => string.Equals(b.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            configuration.ManualBindings.Remove(existing);
        }

        configuration.ManualBindings.Add(new ManualBindingEntry
        {
            ItemId = itemId,
            AnimeId = animeId,
            AnimeName = animeName,
            Slug = slug,
            BoundAt = DateTime.UtcNow,
            Source = source
        });

        plugin.SaveConfiguration();
    }

    private static int DeleteThemeFiles(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ContainingFolderPath))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var directory in new[] { ThemeMusicDirectory, ThemeVideoDirectory })
        {
            var path = Path.Combine(item.ContainingFolderPath, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(path))
            {
                try
                {
                    System.IO.File.Delete(file);
                    deleted++;
                }
                catch
                {
                    // best-effort
                }
            }
        }

        var rootTheme = Path.Combine(item.ContainingFolderPath, RootThemeSongFileName);
        if (System.IO.File.Exists(rootTheme))
        {
            try
            {
                System.IO.File.Delete(rootTheme);
                deleted++;
            }
            catch
            {
                // best-effort
            }
        }

        return deleted;
    }
}
