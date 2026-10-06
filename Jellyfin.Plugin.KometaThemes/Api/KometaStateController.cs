using System;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.KometaThemes.Caching;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Library;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.YouTube;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Overall state, settings, the full check and the activity list.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("KometaThemes")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class KometaStateController : KometaControllerBase
{
    private readonly LibraryScope _scope;
    private readonly LibraryIndex _index;
    private readonly SyncRunner _runner;
    private readonly ActivityLog _activity;
    private readonly IResolutionCache _cache;
    private readonly FailedItemsStore _problems;
    private readonly YouTubeImportService _youTube;

    /// <summary>
    /// Initializes a new instance of the <see cref="KometaStateController"/> class.
    /// </summary>
    /// <param name="scope">Library scope.</param>
    /// <param name="index">Library index.</param>
    /// <param name="runner">Check runner.</param>
    /// <param name="activity">Activity list.</param>
    /// <param name="cache">Resolution cache.</param>
    /// <param name="problems">Problem store.</param>
    /// <param name="youTube">YouTube import.</param>
    public KometaStateController(LibraryScope scope, LibraryIndex index, SyncRunner runner, ActivityLog activity, IResolutionCache cache, FailedItemsStore problems, YouTubeImportService youTube)
    {
        _scope = scope;
        _index = index;
        _runner = runner;
        _activity = activity;
        _cache = cache;
        _problems = problems;
        _youTube = youTube;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets the overview the page opens with.
    /// </summary>
    /// <returns>Version, setup state, libraries, counts, check status.</returns>
    [HttpGet("State")]
    public async System.Threading.Tasks.Task<ActionResult> GetState()
    {
        var config = Config;
        var summaries = await _index.GetAsync(config, HttpContext.RequestAborted).ConfigureAwait(false);
        var youTube = _youTube.GetAvailability();
        return Json(new
        {
            version = Plugin.Instance?.Version.ToString() ?? "2.0.0.0",
            setupRequired = config.SetupCompletedVersion < PluginConfiguration.CurrentSchemaVersion,
            whatsNew = !config.WhatsNewDismissed && config.SetupCompletedVersion >= PluginConfiguration.CurrentSchemaVersion,
            libraries = _scope.GetLibraries(config),
            counts = new
            {
                total = summaries.Count,
                ready = summaries.Count(s => s.Status == "ready"),
                attention = summaries.Count(s => s.Status is "attention" or "failed"),
                pending = summaries.Count(s => s.Status == "pending"),
                empty = summaries.Count(s => s.Status == "empty"),
                excluded = summaries.Count(s => s.Status == "excluded"),
                manual = summaries.Count(s => s.Manual),
            },
            sync = _runner.Status,
            lastCheck = new { utc = config.LastFullSyncUtc, summary = config.LastSyncSummary },
            youTube = new { enabled = config.EnableYouTubeImport, backend = youTube.Backend, available = youTube.Available, error = youTube.Error },
        });
    }

    /// <summary>
    /// Gets the settings.
    /// </summary>
    /// <returns>Settings.</returns>
    [HttpGet("Settings")]
    public ActionResult GetSettings() => Json(ToDto(Config));

    /// <summary>
    /// Saves the settings. Lists (exclusions, matches) are never touched here, so a save cannot undo
    /// a change made from another page in the meantime.
    /// </summary>
    /// <param name="settings">New settings.</param>
    /// <returns>The saved settings.</returns>
    [HttpPost("Settings")]
    [Consumes(MediaTypeNames.Application.Json)]
    public ActionResult SaveSettings([FromBody] SettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var known = _scope.GetLibraries(Config).Select(l => l.Id).ToHashSet();
        var libraries = settings.LibraryIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(known.Contains)
            .Distinct()
            .Select(id => id.ToString("D", CultureInfo.InvariantCulture))
            .ToList();

        Plugin.MutateConfiguration(c =>
        {
            c.LibraryIds.Clear();
            foreach (var id in libraries)
            {
                c.LibraryIds.Add(id);
            }

            c.LibraryPattern = null;
            Apply(c.AudioSettings, settings.Audio);
            Apply(c.VideoSettings, settings.Video);
            c.MaxThemesPerSeason = settings.MaxPerFolder;
            c.PerSeasonThemes = settings.PerSeason;
            c.AutoSyncOnItemAdded = settings.AutoOnAdd;
            c.CleanupThemesOnItemRemoved = settings.CleanupOnRemove;
            c.EnableTitleFallback = settings.TitleFallback;
            c.TitleMatchThreshold = settings.TitleThreshold / 100.0;
            if (settings.ProviderPriority.Count > 0)
            {
                c.ProviderPriority = Sites.NormalizeProviderPriority(settings.ProviderPriority);
            }

            c.RateLimitPerMinute = settings.RateLimit;
            c.PositiveCacheTtlDays = settings.MatchCacheDays;
            c.NegativeCacheTtlHours = settings.MissCacheHours;
            c.DownloadTimeoutSeconds = settings.ConvertTimeout;
            c.DegreeOfParallelism = settings.Parallel;
            c.EnableYouTubeImport = settings.YouTube;
            c.YtDlpPath = string.IsNullOrWhiteSpace(settings.YtDlpPath) ? null : settings.YtDlpPath.Trim();
            c.NormalizeBounds();
        });

        _index.Invalidate();
        return Json(ToDto(Config));
    }

    /// <summary>
    /// Marks the first-run setup as done.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("Setup/Complete")]
    public ActionResult CompleteSetup()
    {
        Plugin.MutateConfiguration(c =>
        {
            c.SetupCompletedVersion = PluginConfiguration.CurrentSchemaVersion;
            c.WhatsNewDismissed = true;
        });
        return NoContent();
    }

    /// <summary>
    /// Hides the "what's new" note.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("WhatsNew/Dismiss")]
    public ActionResult DismissWhatsNew()
    {
        Plugin.MutateConfiguration(c => c.WhatsNewDismissed = true);
        return NoContent();
    }

    /// <summary>
    /// Gets the status of the full check.
    /// </summary>
    /// <returns>Status.</returns>
    [HttpGet("Check")]
    public ActionResult GetCheck() => Json(_runner.Status);

    /// <summary>
    /// Starts the full check.
    /// </summary>
    /// <param name="request">Options.</param>
    /// <returns>Status.</returns>
    [HttpPost("Check")]
    public ActionResult StartCheck([FromBody] CheckRequest? request)
    {
        if (_scope.SelectedIds(Config).Count == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "Choose at least one library in Settings first.");
        }

        if (!_runner.TryStart(request?.RetryProblems ?? false))
        {
            return Error(StatusCodes.Status409Conflict, "A check is already running.");
        }

        return Json(_runner.Status);
    }

    /// <summary>
    /// Stops the full check after the anime in progress.
    /// </summary>
    /// <returns>Status.</returns>
    [HttpDelete("Check")]
    public ActionResult StopCheck()
    {
        _runner.Cancel();
        return Json(_runner.Status);
    }

    /// <summary>
    /// Gets the latest activity.
    /// </summary>
    /// <param name="limit">Most entries.</param>
    /// <param name="itemId">Only this item, if set.</param>
    /// <returns>Entries, newest first.</returns>
    [HttpGet("Activity")]
    public ActionResult GetActivity([FromQuery] int limit = 50, [FromQuery] Guid? itemId = null)
        => Json(_activity.Latest(limit, itemId));

    /// <summary>
    /// Forgets every cached lookup and makes waiting anime due again.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("Cache/Clear")]
    public ActionResult ClearCache()
    {
        _cache.Clear();
        _problems.ResetSchedules();
        _index.Invalidate();
        return NoContent();
    }

    private static SettingsDto ToDto(PluginConfiguration c) => new()
    {
        LibraryIds = c.LibraryIds.ToList(),
        Audio = ToDto(c.AudioSettings),
        Video = ToDto(c.VideoSettings),
        MaxPerFolder = c.MaxThemesPerSeason,
        PerSeason = c.PerSeasonThemes,
        AutoOnAdd = c.AutoSyncOnItemAdded,
        CleanupOnRemove = c.CleanupThemesOnItemRemoved,
        TitleFallback = c.EnableTitleFallback,
        TitleThreshold = (int)Math.Round(c.TitleMatchThreshold * 100),
        ProviderPriority = Sites.NormalizeProviderPriority(c.ProviderPriority).ToList(),
        RateLimit = c.RateLimitPerMinute,
        MatchCacheDays = c.PositiveCacheTtlDays,
        MissCacheHours = c.NegativeCacheTtlHours,
        ConvertTimeout = c.DownloadTimeoutSeconds,
        Parallel = c.DegreeOfParallelism,
        YouTube = c.EnableYouTubeImport,
        YtDlpPath = c.YtDlpPath,
    };

    private static MediaSettingsDto ToDto(MediaTypeConfiguration m) => new()
    {
        Enabled = m.FetchType != FetchType.None,
        Amount = m.FetchType == FetchType.Single ? "one" : "all",
        Openings = !m.IgnoreOPs,
        Endings = !m.IgnoreEDs,
        SkipOverlaps = m.IgnoreOverlapping,
        CreditlessOnly = m.IgnoreThemesWithCredits,
        Volume = m.VolumePercent,
    };

    private static void Apply(MediaTypeConfiguration target, MediaSettingsDto source)
    {
        target.FetchType = !source.Enabled ? FetchType.None : string.Equals(source.Amount, "one", StringComparison.OrdinalIgnoreCase) ? FetchType.Single : FetchType.All;
        target.IgnoreOPs = !source.Openings;
        target.IgnoreEDs = !source.Endings;
        target.IgnoreOverlapping = source.SkipOverlaps;
        target.IgnoreThemesWithCredits = source.CreditlessOnly;
        target.Volume = Math.Clamp(source.Volume, 0, 100) / 100.0;
    }
}
