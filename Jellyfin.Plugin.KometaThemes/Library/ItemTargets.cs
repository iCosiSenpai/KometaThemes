using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.KometaThemes.Library;

/// <summary>
/// A season folder that can hold its own themes.
/// </summary>
/// <param name="Season">The season.</param>
/// <param name="Folder">Its folder.</param>
/// <param name="Info">Number, premiere and episodes.</param>
public sealed record SeasonTarget(Season Season, ThemeFolder Folder, LibrarySeason Info);

/// <summary>
/// The folders of a series or movie that receive themes.
/// </summary>
/// <param name="Owner">The series or movie.</param>
/// <param name="Root">The series or movie folder; null when themes cannot go there.</param>
/// <param name="Seasons">Season folders, in season order.</param>
/// <param name="AllSeasons">Every season numbered 1 or higher, with a folder of its own or not, for season matching.</param>
/// <param name="Note">Why <paramref name="Root"/> is null, if it is.</param>
public sealed record ItemTargets(BaseItem Owner, ThemeFolder? Root, IReadOnlyList<SeasonTarget> Seasons, IReadOnlyList<SeasonInfo> AllSeasons, string? Note);

/// <summary>
/// Works out where an item's themes go.
/// </summary>
public sealed class ItemTargetFinder
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemTargetFinder"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    public ItemTargetFinder(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Lists the folders of a series or movie.
    /// </summary>
    /// <param name="owner">Series or movie.</param>
    /// <returns>The targets.</returns>
    public ItemTargets For(BaseItem owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (owner is Movie movie)
        {
            if (movie.IsInMixedFolder)
            {
                // Jellyfin does not read theme folders next to a movie that shares its folder with
                // others, so files written there would only clutter the folder. 1.x wrote them anyway.
                return new ItemTargets(owner, null, [], [], "This movie shares its folder with other movies, where Jellyfin does not look for themes. Move it into its own folder.");
            }

            return Folder(owner) is { } movieFolder
                ? new ItemTargets(owner, new ThemeFolder(owner.Id, movieFolder, 0, true), [], [], null)
                : new ItemTargets(owner, null, [], [], "Its folder is not available.");
        }

        if (owner is not Series series || Folder(series) is not { } seriesFolder)
        {
            return new ItemTargets(owner, null, [], [], "Its folder is not available.");
        }

        var all = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentId = series.Id,
                IncludeItemTypes = [BaseItemKind.Season],
                IsVirtualItem = false,
            })
            .OfType<Season>()
            .Where(season => season.IndexNumber is > 0)
            .OrderBy(season => season.IndexNumber)
            .Select(season => (Season: season, Path: Folder(season), Info: Describe(season)))
            .ToList();

        var targets = all
            .Where(s => s.Path != null && !SamePath(s.Path, seriesFolder))
            .Select(s => new SeasonTarget(s.Season, new ThemeFolder(s.Season.Id, s.Path!, s.Season.IndexNumber!.Value, false), s.Info))
            .ToList();
        var infos = all
            .Select(s => new SeasonInfo(s.Season.Id, s.Info, targets.Any(t => t.Season.Id == s.Season.Id)))
            .ToList();

        return new ItemTargets(owner, new ThemeFolder(owner.Id, seriesFolder, 0, true), targets, infos, null);
    }

    private LibrarySeason Describe(Season season)
    {
        var episodes = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = season.Id,
            IncludeItemTypes = [BaseItemKind.Episode],
            IsVirtualItem = false,
            Recursive = true,
        });

        var firstAired = season.PremiereDate
            ?? episodes.Where(e => e.PremiereDate.HasValue).Select(e => e.PremiereDate).Min();
        var year = firstAired?.Year ?? season.ProductionYear;
        return new LibrarySeason(season.IndexNumber ?? 0, year, firstAired?.Month, episodes.Count);
    }

    private static string? Folder(BaseItem item)
    {
        var path = item.IsFolder ? item.Path : item.ContainingFolderPath;
        return !string.IsNullOrWhiteSpace(path) && item.IsFileProtocol && Directory.Exists(path) ? path : null;
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.Ordinal);
}
