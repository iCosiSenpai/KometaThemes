using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;

#pragma warning disable CA2007, SA1611, SA1615, CS1591

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST API for playlist management.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Playlist")]
[Produces(MediaTypeNames.Application.Json)]
public class KometaThemesPlaylistController : ControllerBase
{
    private readonly PlaylistManager _playlistManager;
    private readonly ILogger<KometaThemesPlaylistController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesPlaylistController"/> class.
    /// </summary>
    public KometaThemesPlaylistController(
        PlaylistManager playlistManager,
        ILogger<KometaThemesPlaylistController> logger)
    {
        _playlistManager = playlistManager;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes the global anime themes playlist.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> RefreshPlaylist()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var playlistName = configuration.PlaylistName ?? "Anime Themes";

        try
        {
            await _playlistManager.RefreshPlaylistAsync(playlistName, CancellationToken.None);
            return Ok(new { message = $"Playlist '{playlistName}' refreshed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing playlist");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Exports a downloadable M3U playlist with all downloaded themes.
    /// </summary>
    [HttpGet("export")]
    [Produces("audio/x-mpegurl")]
    public async Task<IActionResult> Export()
    {
        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var playlistName = configuration.PlaylistName ?? "Anime Themes";
        var root = configuration.PlaylistExportRoot;

        if (string.IsNullOrWhiteSpace(root))
        {
            root = "Anime";
        }

        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#PLAYLIST:" + playlistName);

        try
        {
            var files = await _playlistManager.CollectAllThemeFilesAsync(root).ConfigureAwait(false);
            foreach (var f in files)
            {
                sb.AppendLine("#EXTINF:-1," + Path.GetFileNameWithoutExtension(f));
                sb.AppendLine(f);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to collect theme files for M3U export");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "audio/x-mpegurl", SanitizeFileName(playlistName) + ".m3u");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            foreach (var c in invalid)
            {
                if (chars[i] == c)
                {
                    chars[i] = '_';
                }
            }
        }

        return new string(chars);
    }
}
