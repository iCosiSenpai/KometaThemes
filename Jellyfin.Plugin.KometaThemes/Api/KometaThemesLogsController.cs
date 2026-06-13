using System;
using System.IO;
using System.Net.Mime;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API exposing the plugin's own entries from the Jellyfin server log,
/// powering the dashboard activity log panel.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Logs")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesLogsController : ControllerBase
{
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<KometaThemesLogsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesLogsController"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths for locating the log directory.</param>
    /// <param name="logger">Logger instance.</param>
    public KometaThemesLogsController(
        IApplicationPaths applicationPaths,
        ILogger<KometaThemesLogsController> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    /// <summary>
    /// Gets the newest plugin log entries from the current Jellyfin server log file.
    /// </summary>
    /// <param name="lines">Maximum number of entries to return (1-1000, default 200).</param>
    /// <returns>The source file name and the parsed entries, oldest first.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetLogs([FromQuery] int lines = 200)
    {
        try
        {
            var newest = PluginLogReader.FindNewestLogFile(_applicationPaths.LogDirectoryPath);
            if (newest == null)
            {
                return Ok(new { file = (string?)null, entries = Array.Empty<PluginLogEntry>() });
            }

            var entries = PluginLogReader.ReadPluginEntries(newest, lines);
            return Ok(new { file = Path.GetFileName(newest), entries });
        }
        catch (Exception ex)
        {
            // Never 500 the dashboard for a log-format or IO hiccup.
            _logger.LogWarning(ex, "Failed to read plugin entries from the server log");
            return Ok(new { file = (string?)null, entries = Array.Empty<PluginLogEntry>() });
        }
    }
}
