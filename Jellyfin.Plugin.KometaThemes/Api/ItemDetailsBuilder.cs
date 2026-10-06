using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Library;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;
using Jellyfin.Plugin.KometaThemes.Sync;
using Jellyfin.Plugin.KometaThemes.Themes;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Builds what the anime page shows: matches, themes and files of the item and its seasons.
/// </summary>
public sealed class ItemDetailsBuilder
{
    private readonly AnimeResolver _resolver;
    private readonly SeasonResolver _seasons;
    private readonly ItemTargetFinder _targets;
    private readonly FolderStateStore _store;
    private readonly LibraryIndex _index;
    private readonly FailedItemsStore _problems;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemDetailsBuilder"/> class.
    /// </summary>
    /// <param name="resolver">Anime resolver.</param>
    /// <param name="seasons">Season resolver.</param>
    /// <param name="targets">Target finder.</param>
    /// <param name="store">State file store.</param>
    /// <param name="index">Library index.</param>
    /// <param name="problems">Problem store.</param>
    public ItemDetailsBuilder(AnimeResolver resolver, SeasonResolver seasons, ItemTargetFinder targets, FolderStateStore store, LibraryIndex index, FailedItemsStore problems)
    {
        _resolver = resolver;
        _seasons = seasons;
        _targets = targets;
        _store = store;
        _index = index;
        _problems = problems;
    }

    /// <summary>
    /// Converts an anime entry for the page.
    /// </summary>
    /// <param name="anime">The entry.</param>
    /// <param name="score">Match score, if ranked.</param>
    /// <returns>The DTO.</returns>
    public static AnimeDto ToDto(Anime anime, int? score = null)
    {
        ArgumentNullException.ThrowIfNull(anime);
        return new AnimeDto(
            anime.Id,
            anime.Name,
            anime.Slug,
            anime.Year,
            anime.Season,
            anime.MediaFormat,
            anime.GetCover(large: false),
            "https://animethemes.moe/anime/" + Uri.EscapeDataString(anime.Slug ?? string.Empty),
            (anime.Themes ?? []).Count(t => t.Type is ThemeType.OP or ThemeType.ED),
            score);
    }

    /// <summary>
    /// Lists an entry's themes without folder state, for previews in the match dialog.
    /// </summary>
    /// <param name="anime">The entry.</param>
    /// <returns>Themes.</returns>
    public static IReadOnlyList<ThemeDto> Themes(Anime anime)
        => ThemeCatalog.All([anime]).Select(choice => ToDto(choice, Off(choice, MediaType.Audio), Off(choice, MediaType.Video))).ToList();

    /// <summary>
    /// Builds the page data of a series or movie.
    /// </summary>
    /// <param name="owner">Series or movie.</param>
    /// <param name="config">Settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page data.</returns>
    public async Task<ItemDetailDto> BuildAsync(BaseItem owner, PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(config);

        var resolution = (await _resolver.ResolveAsync([owner], config, cancellationToken).ConfigureAwait(false))[owner.Id];
        var targets = _targets.For(owner);
        var folders = new List<FolderDto>();

        if (targets.Root != null)
        {
            var label = owner is MediaBrowser.Controller.Entities.Movies.Movie ? "Movie" : "Series";
            folders.Add(await FolderAsync(owner.Id, label, 0, 0, owner.ProductionYear, targets.Root.Folder, resolution, config, cancellationToken).ConfigureAwait(false));
        }

        foreach (var season in targets.Seasons)
        {
            var seasonResolution = await _seasons.ResolveAsync(owner, resolution, season.Season.Id, season.Info, config, cancellationToken).ConfigureAwait(false);
            folders.Add(await FolderAsync(
                season.Season.Id,
                season.Season.Name ?? "Season " + season.Info.Number,
                season.Info.Number,
                season.Info.EpisodeCount,
                season.Info.Year,
                season.Folder.Folder,
                seasonResolution,
                config,
                cancellationToken).ConfigureAwait(false));
        }

        _index.Invalidate(owner.Id);
        var summary = await _index.SummarizeAsync(owner, config, cancellationToken).ConfigureAwait(false);
        var problem = _problems.Get(owner.Id);
        return new ItemDetailDto(
            owner.Id,
            owner.Name ?? string.Empty,
            owner.ProductionYear,
            owner.GetBaseItemKind().ToString(),
            summary,
            problem == null ? null : new ProblemDto(problem.Reason.ToString(), problem.Error, problem.Attempts, problem.NextAttemptUtc),
            folders,
            targets.Note);
    }

    private async Task<FolderDto> FolderAsync(
        Guid itemId,
        string label,
        int seasonNumber,
        int episodes,
        int? year,
        string folder,
        Resolution resolution,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        var animes = resolution.State == MatchState.Matched ? resolution.Animes : [];
        var audio = ThemeCatalog.Select(animes, config.AudioSettings, config.MaxThemesPerSeason).Select(c => c.Theme.Id).ToHashSet();
        var video = ThemeCatalog.Select(animes, config.VideoSettings, config.MaxThemesPerSeason).Select(c => c.Theme.Id).ToHashSet();

        var themes = ThemeCatalog.All(animes)
            .Select(choice => ToDto(
                choice,
                State(choice, MediaType.Audio, audio.Contains(choice.Theme.Id), state, folder),
                State(choice, MediaType.Video, video.Contains(choice.Theme.Id), state, folder)))
            .ToList();

        var match = new MatchDto(
            resolution.State switch
            {
                MatchState.Matched => "matched",
                MatchState.Inherit => "inherit",
                MatchState.NotFound => "notFound",
                _ => "unavailable"
            },
            resolution.Method,
            resolution.Manual,
            animes.Select(a => ToDto(a)).ToList());

        return new FolderDto(itemId, label, seasonNumber, episodes, year, match, themes, Files(folder, state), state.CheckedUtc);
    }

    private static MediaStateDto State(ThemeChoice choice, MediaType media, bool bySettings, FolderState state, string folder)
    {
        var choiceValue = state.OverrideFor(choice.Theme.Id, media);
        var record = state.Records.FirstOrDefault(r =>
            r.Source == ThemeSource.AnimeThemes
            && r.ThemeId == choice.Theme.Id
            && r.Media == media
            && File.Exists(Path.Combine(folder, r.EffectiveDirectory, r.FileName)));
        return new MediaStateDto(choiceValue ?? bySettings, bySettings, choiceValue, record?.FileName, choice.Link(media));
    }

    private static MediaStateDto Off(ThemeChoice choice, MediaType media)
        => new(false, false, null, null, choice.Link(media));

    private static ThemeDto ToDto(ThemeChoice choice, MediaStateDto audio, MediaStateDto video)
        => new(
            choice.Theme.Id,
            choice.Anime.Id,
            choice.Code,
            choice.Theme.Type.ToString(),
            choice.Title,
            choice.Artists,
            choice.Episodes,
            MergeRanges(EpisodeRangeParser.Parse(choice.Episodes)),
            audio,
            video,
            choice.Video.Creditless,
            choice.Video.Overlap.ToString(),
            choice.Video.Source.ToString(),
            choice.Video.Resolution,
            choice.Entry.Spoiler,
            choice.Entry.Nsfw);

    /// <summary>
    /// Sorts episode ranges and joins the ones that touch, so "2-3, 4, 6-8, 9-10" reads as 2-4, 6-10.
    /// </summary>
    /// <param name="ranges">Parsed ranges.</param>
    /// <returns>Start-end pairs.</returns>
    internal static List<int[]> MergeRanges(IEnumerable<EpisodeRange> ranges)
    {
        var merged = new List<int[]>();
        foreach (var range in ranges.OrderBy(r => r.StartEpisode).ThenBy(r => r.EndEpisode))
        {
            if (merged.Count > 0 && range.StartEpisode <= merged[^1][1] + 1)
            {
                merged[^1][1] = Math.Max(merged[^1][1], range.EndEpisode);
            }
            else
            {
                merged.Add([range.StartEpisode, range.EndEpisode]);
            }
        }

        return merged;
    }

    private static List<FileDto> Files(string folder, FolderState state)
    {
        var files = new List<FileDto>();
        foreach (var directory in new[] { ThemeFileKinds.AudioDirectory, ThemeFileKinds.VideoDirectory })
        {
            var path = Path.Combine(folder, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in ThemeFileKinds.EnumerateFiles(path, directory == ThemeFileKinds.AudioDirectory).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileName(file);
                var record = state.Records.FirstOrDefault(r => string.Equals(r.EffectiveDirectory, directory, StringComparison.Ordinal) && string.Equals(r.FileName, name, StringComparison.Ordinal));
                var source = record == null ? "yours" : record.Source == ThemeSource.YouTube ? "youtube" : "animethemes";
                var code = record == null ? null : (record.Type == ThemeType.ED ? "ED" : "OP") + record.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                files.Add(new FileDto(directory, name, source, record?.Title, record?.Artists, code, new FileInfo(file).Length));
            }
        }

        return files;
    }
}
