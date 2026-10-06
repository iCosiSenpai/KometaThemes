using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Configuration;
using Jellyfin.Plugin.KometaThemes.Models;

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
/// Picks the version of each theme and the themes to download, from animethemes.moe data alone.
/// </summary>
public static class ThemeCatalog
{
    /// <summary>
    /// Lists every opening and ending with its best version, for display.
    /// </summary>
    /// <param name="animes">One anime, or several when a season spans more than one entry.</param>
    /// <returns>Themes in airing order: openings by sequence, then endings.</returns>
    public static IReadOnlyList<ThemeChoice> All(IEnumerable<Anime> animes)
        => Candidates(animes, null).ToList();

    /// <summary>
    /// Chooses what to download for one media type.
    /// </summary>
    /// <param name="animes">Anime entries of the target.</param>
    /// <param name="settings">Settings of the media type.</param>
    /// <param name="maxPerTarget">Most themes to keep.</param>
    /// <returns>The themes to download, possibly none.</returns>
    public static IReadOnlyList<ThemeChoice> Select(IEnumerable<Anime> animes, MediaTypeConfiguration settings, int maxPerTarget)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return [];
        }

        var eligible = Candidates(animes, settings)
            .Where(choice => !(settings.IgnoreOPs && choice.Theme.Type == ThemeType.OP))
            .Where(choice => !(settings.IgnoreEDs && choice.Theme.Type == ThemeType.ED))
            .ToList();

        return settings.FetchType == FetchType.Single
            ? eligible.Take(1).ToList()
            : eligible.Take(Math.Max(1, maxPerTarget)).ToList();
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
    /// Scores a version: lower is better. Clean, creditless, unspoiled, safe, good-source first.
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

        // Among otherwise equal encodes prefer the first version and a higher resolution.
        penalty += Math.Max(0, (entry.Version ?? 1) - 1);
        penalty += video.Resolution is >= 1080 ? 0 : 1;
        return penalty;
    }

    private static IEnumerable<ThemeChoice> Candidates(IEnumerable<Anime> animes, MediaTypeConfiguration? settings)
        => Unique(animes, settings)
            .OrderBy(choice => OrderOf(animes, choice.Anime))
            .ThenBy(choice => choice.Theme.Type)
            .ThenBy(choice => choice.Theme.Sequence ?? 1)
            .ThenBy(choice => choice.Theme.Slug, StringComparer.OrdinalIgnoreCase);

    private static int OrderOf(IEnumerable<Anime> animes, Anime anime)
    {
        var index = 0;
        foreach (var candidate in animes)
        {
            if (candidate.Id == anime.Id)
            {
                return index;
            }

            index++;
        }

        return index;
    }

    private static IEnumerable<ThemeChoice> Unique(IEnumerable<Anime> animes, MediaTypeConfiguration? settings)
    {
        ArgumentNullException.ThrowIfNull(animes);

        foreach (var anime in animes)
        {
            var best = (anime.Themes ?? [])
                .Where(theme => theme is { Type: ThemeType.OP or ThemeType.ED })
                .Select(theme => (Theme: theme, Choice: BestVersion(anime, theme, settings)))
                .Where(pair => pair.Choice != null)
                .Select(pair => pair.Choice!)
                .OrderBy(choice => choice.Theme.Type)
                .ThenBy(choice => choice.Theme.Sequence ?? 1)
                .ThenBy(choice => choice.Theme.Slug, StringComparer.OrdinalIgnoreCase);

            // The same song listed again under a variant code (ED1 and ED1-EN, an English dub; OP1
            // and OP1-BD) would play twice: keep the plain code, or the first variant left after filtering.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var choice in best.OrderBy(c => c.Code.Contains('-', StringComparison.Ordinal) ? 1 : 0).ThenBy(c => c.Theme.Type).ThenBy(c => c.Theme.Sequence ?? 1))
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
                    if (settings.IgnoreOverlapping && video.Overlap != OverlapType.None)
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
