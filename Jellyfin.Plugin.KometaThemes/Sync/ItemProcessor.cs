using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Library;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Result of one season.
/// </summary>
/// <param name="SeasonNumber">Season number.</param>
/// <param name="Resolution">How the season was resolved.</param>
/// <param name="Result">What changed in its folder, or null when nothing was attempted.</param>
public sealed record SeasonReport(int SeasonNumber, Resolution Resolution, FolderResult? Result);

/// <summary>
/// Result of processing one series or movie.
/// </summary>
/// <param name="Resolution">How the item was resolved.</param>
/// <param name="Root">What changed in its own folder.</param>
/// <param name="Seasons">Per-season results.</param>
/// <param name="Note">Why nothing could be written, if so.</param>
public sealed record ItemReport(Resolution Resolution, FolderResult? Root, IReadOnlyList<SeasonReport> Seasons, string? Note)
{
    /// <summary>Gets files downloaded in all folders.</summary>
    public int Downloaded => (Root?.Downloaded ?? 0) + Seasons.Sum(s => s.Result?.Downloaded ?? 0);

    /// <summary>Gets files removed in all folders.</summary>
    public int Deleted => (Root?.Deleted ?? 0) + Seasons.Sum(s => s.Result?.Deleted ?? 0);

    /// <summary>Gets files renamed in all folders.</summary>
    public int Renamed => (Root?.Renamed ?? 0) + Seasons.Sum(s => s.Result?.Renamed ?? 0);

    /// <summary>Gets files that failed in all folders.</summary>
    public int Failed => (Root?.Failed ?? 0) + Seasons.Sum(s => s.Result?.Failed ?? 0);

    /// <summary>Gets every error message.</summary>
    public IEnumerable<string> Errors => (Root?.Errors ?? []).Concat(Seasons.SelectMany(s => s.Result?.Errors ?? []));
}

/// <summary>
/// Brings one series or movie up to date: its own folder and, for a series, every season folder.
/// </summary>
public sealed class ItemProcessor
{
    private readonly ItemTargetFinder _targets;
    private readonly AnimeResolver _resolver;
    private readonly SeasonResolver _seasons;
    private readonly ThemeInstaller _installer;
    private readonly ItemRefresher _refresher;
    private readonly FailedItemsStore _problems;
    private readonly ActivityLog _activity;
    private readonly LibraryIndex _index;
    private readonly ILogger<ItemProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemProcessor"/> class.
    /// </summary>
    /// <param name="targets">Target finder.</param>
    /// <param name="resolver">Anime resolver.</param>
    /// <param name="seasons">Season resolver.</param>
    /// <param name="installer">Theme installer.</param>
    /// <param name="refresher">Item refresher.</param>
    /// <param name="problems">Problem store.</param>
    /// <param name="activity">Activity list.</param>
    /// <param name="index">Library summary index.</param>
    /// <param name="logger">Logger.</param>
    public ItemProcessor(
        ItemTargetFinder targets,
        AnimeResolver resolver,
        SeasonResolver seasons,
        ThemeInstaller installer,
        ItemRefresher refresher,
        FailedItemsStore problems,
        ActivityLog activity,
        LibraryIndex index,
        ILogger<ItemProcessor> logger)
    {
        _targets = targets;
        _resolver = resolver;
        _seasons = seasons;
        _installer = installer;
        _refresher = refresher;
        _problems = problems;
        _activity = activity;
        _index = index;
        _logger = logger;
    }

    /// <summary>
    /// Resolves and processes one item.
    /// </summary>
    /// <param name="owner">Series or movie.</param>
    /// <param name="config">Settings.</param>
    /// <param name="redownload">Whether to download every wanted file again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    public async Task<ItemReport> ProcessAsync(BaseItem owner, PluginConfiguration config, bool redownload, CancellationToken cancellationToken)
    {
        var resolutions = await _resolver.ResolveAsync([owner], config, cancellationToken).ConfigureAwait(false);
        return await ProcessAsync(owner, resolutions[owner.Id], config, redownload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes one item that is already resolved.
    /// </summary>
    /// <param name="owner">Series or movie.</param>
    /// <param name="resolution">Its resolution.</param>
    /// <param name="config">Settings.</param>
    /// <param name="redownload">Whether to download every wanted file again.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report.</returns>
    public async Task<ItemReport> ProcessAsync(BaseItem owner, Resolution resolution, PluginConfiguration config, bool redownload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            return await ProcessCoreAsync(owner, resolution, config, redownload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _index.Invalidate(owner.Id);
        }
    }

    private async Task<ItemReport> ProcessCoreAsync(BaseItem owner, Resolution resolution, PluginConfiguration config, bool redownload, CancellationToken cancellationToken)
    {
        switch (resolution.State)
        {
            case MatchState.Unavailable:
                // Transient: change nothing on disk and record nothing; the next check tries again.
                return new ItemReport(resolution, null, [], resolution.Method);
            case MatchState.NotFound:
            case MatchState.Inherit:
                var first = _problems.Get(owner.Id) == null;
                _problems.Record(owner, ProblemKind.Unresolved, resolution.Method);
                if (first)
                {
                    _activity.Add(ActivityKind.NotFound, owner.Id, owner.Name, "No match on animethemes.moe. Find it by hand from its page.");
                }

                return new ItemReport(resolution, null, [], resolution.Method);
        }

        var targets = _targets.For(owner);
        FolderResult? root = null;
        if (targets.Root != null)
        {
            root = await _installer.ApplyAsync(targets.Root, resolution.Animes, config, redownload, cancellationToken).ConfigureAwait(false);
        }

        var seasonReports = new List<SeasonReport>();
        foreach (var season in targets.Seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seasonResolution = await _seasons.ResolveAsync(owner, resolution, season.Season.Id, season.Info, config, cancellationToken).ConfigureAwait(false);
            if (seasonResolution.State == MatchState.Unavailable)
            {
                seasonReports.Add(new SeasonReport(season.Info.Number, seasonResolution, null));
                continue;
            }

            // A season without its own entry holds no theme files: Jellyfin plays the series themes
            // there. Applying an empty list also clears what 1.x wrongly put in season folders.
            var animes = seasonResolution.State == MatchState.Matched ? seasonResolution.Animes : [];
            var result = await _installer.ApplyAsync(season.Folder, animes, config, redownload, cancellationToken).ConfigureAwait(false);
            seasonReports.Add(new SeasonReport(season.Info.Number, seasonResolution, result));
            if (result.Changed)
            {
                await _refresher.RefreshAsync(season.Season, cancellationToken).ConfigureAwait(false);
            }
        }

        if (root?.Changed == true)
        {
            await _refresher.RefreshAsync(owner, cancellationToken).ConfigureAwait(false);
        }

        var report = new ItemReport(resolution, root, seasonReports, targets.Note);
        Record(owner, report);
        return report;
    }

    private void Record(BaseItem owner, ItemReport report)
    {
        if (report.Failed > 0)
        {
            var errors = string.Join("; ", report.Errors.Take(3));
            _problems.Record(owner, ProblemKind.DownloadFailed, errors);
            _activity.Add(ActivityKind.Failed, owner.Id, owner.Name, string.Create(CultureInfo.InvariantCulture, $"{report.Failed} file(s) could not be downloaded: {errors}"));
        }
        else
        {
            _problems.Remove(owner.Id);
        }

        if (report.Downloaded > 0)
        {
            var names = (report.Root?.DownloadedNames ?? []).Concat(report.Seasons.SelectMany(s => s.Result?.DownloadedNames ?? [])).Distinct().Take(4).ToList();
            _activity.Add(ActivityKind.Downloaded, owner.Id, owner.Name, string.Create(CultureInfo.InvariantCulture, $"Downloaded {report.Downloaded} file(s): {string.Join(", ", names)}"));
        }

        if (report.Renamed > 0)
        {
            _activity.Add(ActivityKind.Renamed, owner.Id, owner.Name, string.Create(CultureInfo.InvariantCulture, $"Renamed {report.Renamed} file(s) after their songs"));
        }

        if (report.Deleted > 0)
        {
            _activity.Add(ActivityKind.Removed, owner.Id, owner.Name, string.Create(CultureInfo.InvariantCulture, $"Removed {report.Deleted} file(s) that no longer belong here"));
        }

        _logger.LogInformation(
            "{Name}: {Method}; {Downloaded} downloaded, {Renamed} renamed, {Deleted} removed, {Failed} failed",
            owner.Name,
            report.Resolution.Method,
            report.Downloaded,
            report.Renamed,
            report.Deleted,
            report.Failed);
    }
}
