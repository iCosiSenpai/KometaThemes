using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Jellyfin.Plugin.KometaThemes.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for the failed/unresolved items list shown in the dashboard.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Failed")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesFailedController : ControllerBase
{
    private readonly FailedItemsStore _failedItems;
    private readonly ILogger<KometaThemesFailedController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesFailedController"/> class.
    /// </summary>
    /// <param name="failedItems">Failed items store.</param>
    /// <param name="logger">Logger instance.</param>
    public KometaThemesFailedController(
        FailedItemsStore failedItems,
        ILogger<KometaThemesFailedController> logger)
    {
        _failedItems = failedItems;
        _logger = logger;
    }

    /// <summary>
    /// Lists all failed/unresolved items, newest attempt first.
    /// </summary>
    /// <returns>A list of failed item entries.</returns>
    [HttpGet("items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetFailedItems()
    {
        return Ok(_failedItems.GetAll());
    }

    /// <summary>
    /// Gets the total count of failed items.
    /// </summary>
    /// <returns>The count of failed items.</returns>
    [HttpGet("count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetFailedCount()
    {
        return Ok(new { count = _failedItems.Count });
    }

    /// <summary>
    /// Dismisses a single failed item without retrying or blacklisting it.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID to dismiss.</param>
    /// <returns>A success message or 404 when not tracked.</returns>
    [HttpDelete("items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DismissFailedItem([FromRoute, Required] string itemId)
    {
        if (!_failedItems.Remove(itemId))
        {
            return NotFound(new { error = "Failed item not found" });
        }

        _logger.LogInformation("Dismissed failed item {Id}", itemId);
        return Ok(new { message = "Failed item dismissed" });
    }

    /// <summary>
    /// Marks a failed item as manually resolved without retrying or blacklisting it.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID to mark as resolved.</param>
    /// <returns>A success message or 404 when not tracked.</returns>
    [HttpPost("items/{itemId}/resolve-manually")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ResolveManually([FromRoute, Required] string itemId)
    {
        if (!_failedItems.Remove(itemId))
        {
            return NotFound(new { error = "Failed item not found" });
        }

        _logger.LogInformation("Marked failed item {Id} as manually resolved", itemId);
        return Ok(new { message = "Failed item marked as manually resolved" });
    }

    /// <summary>
    /// Clears the entire failed items list.
    /// </summary>
    /// <returns>A success message with cleared count.</returns>
    [HttpPost("clear")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult ClearFailedItems()
    {
        var removed = _failedItems.Clear();
        _logger.LogInformation("Cleared {Count} failed items", removed);
        return Ok(new { message = $"Cleared {removed} failed items", removed });
    }
}
