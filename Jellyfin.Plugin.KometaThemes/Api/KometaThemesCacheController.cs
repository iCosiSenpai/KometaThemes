using System;
using Jellyfin.Plugin.KometaThemes.Caching;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// REST endpoints for inspecting and clearing the KometaThemes resolution cache.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("Plugins/KometaThemes/Cache")]
public class KometaThemesCacheController : ControllerBase
{
    private readonly IResolutionCache _resolutionCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaThemesCacheController"/> class.
    /// </summary>
    /// <param name="resolutionCache">The shared resolution cache.</param>
    public KometaThemesCacheController(IResolutionCache resolutionCache)
    {
        _resolutionCache = resolutionCache;
    }

    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    /// <returns>Cache statistics.</returns>
    [HttpGet("stats")]
    public ActionResult<CacheStatsResponse> GetStats()
    {
        return Ok(CacheStatsResponse.From(_resolutionCache.GetStats()));
    }

    /// <summary>
    /// Clears the resolution cache and returns the updated statistics.
    /// </summary>
    /// <returns>Cache statistics after clearing.</returns>
    [HttpPost("clear")]
    public ActionResult<CacheStatsResponse> Clear()
    {
        _resolutionCache.Clear();
        return Ok(CacheStatsResponse.From(_resolutionCache.GetStats()));
    }
}
