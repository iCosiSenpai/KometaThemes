using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;
using Jellyfin.Plugin.KometaThemes.Resolving;

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// One theme with the version KometaThemes would use for it.
/// </summary>
/// <param name="Anime">The anime the theme belongs to.</param>
/// <param name="Theme">The theme.</param>
/// <param name="Entry">The chosen version.</param>
/// <param name="Video">The chosen encode.</param>
public sealed record ThemeChoice(Anime Anime, AnimeTheme Theme, AnimeThemeEntry Entry, Video Video)
{
    /// <summary>Gets the short code, such as <c>OP1</c> or <c>ED1-TV</c>.</summary>
    public string Code => ThemeNaming.Code(Theme);

    /// <summary>Gets the song title, if known.</summary>
    public string? Title => string.IsNullOrWhiteSpace(Theme.Song?.Title) ? null : Theme.Song!.Title!.Trim();

    /// <summary>Gets the credited artists for display.</summary>
    public string Artists => Theme.Song?.ArtistNames ?? string.Empty;

    /// <summary>Gets the episodes the theme airs in, merged over all of its versions.</summary>
    public string? Episodes => ThemeCatalog.EpisodesOf(Theme);

    /// <summary>
    /// Gets the download link for a media type.
    /// </summary>
    /// <param name="media">Audio or video.</param>
    /// <returns>The link, or null when that media type is not available.</returns>
    public string? Link(MediaType media) => media == MediaType.Audio ? Video.Audio?.Link : Video.Link;
}

/// <summary>
/// Picks the version of each theme and the themes to download, from animethemes.moe data alone. Pure.
/// </summary>
public static class ThemeCatalog
{
    /// <summary>
    /// Lists every opening and ending with its best version, for display.
    /// </summary>
    /// <param name="animes">One anime, or several when a season spans more than one entry.</param>
    /// <param name="window">Episodes of the entry the folder covers, when several seasons share one entry.</param>
    /// <returns>Themes in airing order: per entry, openings by sequence, then endings.</returns>
    public static IReadOnlyList<ThemeChoice> All(IEnumerable<Anime> animes, EpisodeWindow? window = null)
        => Candidates(animes, null, window).ToList();

    /// <summary>
    /// Chooses what to download for one media type.
    /// </summary>
    /// <param name="animes">Anime entries of the folder.</param>
    /// <param name="settings">Settings of the media type.</param>
    /// <param name="maxPerKind">Most openings, and separately most endings, to keep.</param>
    /// <param name="window">Episodes of the entry the folder covers, when several seasons share one entry.</param>
    /// <returns>The themes to download, possibly none.</returns>
    public static IReadOnlyList<ThemeChoice> Select(IEnumerable<Anime> animes, MediaTypeConfiguration settings, int maxPerKind, EpisodeWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return [];
        }

        var eligible = Candidates(animes, settings, window)
            .Where(choice => !(settings.IgnoreOPs && choice.Theme.Type == ThemeType.OP))
            .Where(choice => !(settings.IgnoreEDs && choice.Theme.Type == ThemeType.ED))
            .ToList();

        if (settings.FetchType == FetchType.Single)
        {
            return eligible.Take(1).ToList();
        }

        // Openings and endings are capped separately: a long series with five openings used to get
        // no ending at all.
        var cap = Math.Max(1, maxPerKind);
        return eligible.Where(c => c.Theme.Type == ThemeType.OP).Take(cap)
            .Concat(eligible.Where(c => c.Theme.Type == ThemeType.ED).Take(cap))
            .OrderBy(c => eligible.IndexOf(c))
            .ToList();
    }

    /// <summary>
    /// Merges the episode lists of all versions of a theme.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <returns>Episodes like <c>1-12</c>, or null when none are listed.</returns>
    public static string? EpisodesOf(AnimeTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var parts = (theme.Entries ?? [])
            .Select(entry => entry.Episodes?.Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// Gets the last episode an entry's themes are listed for, a lower bound of its episode count.
    /// </summary>
    /// <param name="anime">The entry, with its themes.</param>
    /// <returns>The episode, or null when no theme lists episodes.</returns>
    public static int? LastListedEpisode(Anime anime)
    {
        ArgumentNullException.ThrowIfNull(anime);
        var last = (anime.Themes ?? [])
            .SelectMany(theme => EpisodeRangeParser.Parse(EpisodesOf(theme)))
            .Select(range => range.EndEpisode)
            .DefaultIfEmpty(0)
            .Max();
        return last > 0 ? last : null;
    }

    /// <summary>
    /// Tells whether a theme airs in a window of episodes.
    /// </summary>
    /// <remarks>
    /// A theme that lists no episodes is placed in the window that starts the entry, so it is kept
    /// once rather than in every season sharing the entry.
    /// </remarks>
    /// <param name="theme">The theme.</param>
    /// <param name="window">The window, or null for the whole entry.</param>
    /// <returns>True when the theme belongs.</returns>
    public static bool InWindow(AnimeTheme theme, EpisodeWindow? window)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (window == null)
        {
            return true;
        }

        var ranges = EpisodeRangeParser.Parse(EpisodesOf(theme));
        return ranges.Count == 0
            ? window.First <= 1
            : ranges.Any(range => range.StartEpisode <= window.Last && range.EndEpisode >= window.First);
    }

    /// <summary>
    /// Scores a version: lower is better. Clean, creditless, no burned-in text, unspoiled, safe, good-source first.
    /// </summary>
    /// <param name="entry">Theme version.</param>
    /// <param name="video">Encode.</param>
    /// <returns>The penalty.</returns>
    internal static int Penalty(AnimeThemeEntry entry, Video video)
    {
        var penalty = 0;
        penalty += entry.Spoiler ? 50 : 0;
        penalty += entry.Nsfw ? 10 : 0;
        penalty += video.Overlap switch
        {
            OverlapType.Over => 20,
            OverlapType.Transition => 15,
            _ => 0
        };
        penalty += video.Source switch
        {
            VideoSource.LD or VideoSource.VHS => 10,
            VideoSource.WEB or VideoSource.RAW => 5,
            VideoSource.Unknown => 6,
            _ => 0
        };
        penalty += video.Creditless ? 0 : 10;

        // Subtitles burned into the picture: fine on a music page, out of place as a backdrop.
        penalty += video.Subbed ? 8 : 0;
        penalty += video.Lyrics ? 8 : 0;

        // Among otherwise equal encodes prefer the first version and a higher resolution.
        penalty += Math.Max(0, (entry.Version ?? 1) - 1);
        penalty += video.Resolution is >= 1080 ? 0 : 1;
        return penalty;
    }

    private static IEnumerable<ThemeChoice> Candidates(IEnumerable<Anime> animes, MediaTypeConfiguration? settings, EpisodeWindow? window)
    {
        ArgumentNullException.ThrowIfNull(animes);
        var order = animes.ToList();
        return Unique(order, settings, window)
            .OrderBy(choice => order.FindIndex(anime => anime.Id == choice.Anime.Id))
            .ThenBy(choice => choice.Theme.Type)
            .ThenBy(choice => choice.Theme.Sequence ?? 1)
            .ThenBy(choice => choice.Theme.Slug, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<ThemeChoice> Unique(IReadOnlyList<Anime> animes, MediaTypeConfiguration? settings, EpisodeWindow? window)
    {
        foreach (var anime in animes)
        {
            var best = (anime.Themes ?? [])
                .Where(theme => theme is { Type: ThemeType.OP or ThemeType.ED } && InWindow(theme, window))
                .Select(theme => BestVersion(anime, theme, settings))
                .OfType<ThemeChoice>()
                .ToList();

            // The same song listed again under a variant code (ED1 and ED1-EN, an English dub; OP1
            // and OP1-BD) would play twice: keep the plain code, or the first variant left after filtering.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in best
                .OrderBy(c => c.Code.Contains('-', StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(c => c.Theme.Type)
                .ThenBy(c => c.Theme.Sequence ?? 1))
            {
                if (choice.Title == null || seen.Add(choice.Theme.Type + ":" + choice.Title.Trim()))
                {
                    yield return choice;
                }
            }
        }
    }

    private static ThemeChoice? BestVersion(Anime anime, AnimeTheme theme, MediaTypeConfiguration? settings)
    {
        ThemeChoice? best = null;
        var bestPenalty = int.MaxValue;
        foreach (var entry in theme.Entries ?? [])
        {
            foreach (var video in entry.Videos ?? [])
            {
                if (string.IsNullOrWhiteSpace(video.Link) || string.IsNullOrWhiteSpace(video.Audio?.Link))
                {
                    continue;
                }

                if (settings != null)
                {
                    // Episode scenes over the song are skipped. A version that only fades into the
                    // episode is still the song: it is used when there is no clean one.
                    if (settings.IgnoreOverlapping && video.Overlap == OverlapType.Over)
                    {
                        continue;
                    }

                    if (settings.IgnoreThemesWithCredits && !video.Creditless)
                    {
                        continue;
                    }
                }

                var penalty = Penalty(entry, video);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    best = new ThemeChoice(anime, theme, entry, video);
                }
            }
        }

        return best;
    }
}
