using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.AniList;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// What KometaThemes knows about a season in the library.
/// </summary>
/// <param name="Number">Season number.</param>
/// <param name="Year">Premiere year, when known.</param>
/// <param name="Month">Premiere month, when known.</param>
/// <param name="EpisodeCount">Episodes present in the library.</param>
public sealed record LibrarySeason(int Number, int? Year, int? Month, int EpisodeCount);

/// <summary>
/// The anime entries chosen for a season, and why.
/// </summary>
/// <param name="Entries">One entry, or up to three consecutive ones when the library merges cours into one season.</param>
/// <param name="Reason">Plain-language explanation shown to the owner.</param>
public sealed record SeasonMatch(IReadOnlyList<AniListSeason> Entries, string Reason);

/// <summary>
/// Matches a library season to entries of an AniList sequel chain.
/// </summary>
/// <remarks>
/// <para>
/// 1.x split the themes of a single anime by episode ranges and handed the pieces out to seasons, so
/// the second season of a show got the second ending of its first season. Later seasons are in fact
/// separate anime on animethemes.moe; this finds the right one through AniList's sequel relations.
/// </para>
/// <para>
/// Three signals are weighed: premiere year (2 points), episode count (2 points) and position in the
/// chain (1 point). A candidate that any signal contradicts is dropped: a year two or more apart, a
/// month far off in the same year, or more episodes in the library than the entry ever aired. A
/// match needs 3 points and must beat every other candidate; otherwise the season keeps the series
/// themes, which is never wrong, only less specific.
/// </para>
/// </remarks>
public static class SeasonMatcher
{
    private const int YearPoints = 2;
    private const int EpisodePoints = 2;
    private const int IndexPoints = 1;
    private const int MergePenalty = 1;
    private const int RequiredPoints = 3;
    private const int MaxMergedEntries = 3;

    /// <summary>
    /// Picks the chain entries for a season.
    /// </summary>
    /// <param name="chain">Sequel chain; element 0 is the entry the series itself matched.</param>
    /// <param name="season">The library season, number 2 or higher.</param>
    /// <returns>The match, or null when no candidate is convincing.</returns>
    public static SeasonMatch? Match(IReadOnlyList<AniListSeason> chain, LibrarySeason season)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(season);

        if (season.Number < 2 || chain.Count < 2)
        {
            return null;
        }

        var scored = new List<(int Score, int Start, int Length, string Reason)>();
        for (var start = 1; start < chain.Count; start++)
        {
            for (var length = 1; length <= MaxMergedEntries && start + length <= chain.Count; length++)
            {
                var span = chain.Skip(start).Take(length).ToList();
                if (Score(span, start, season) is { } result)
                {
                    scored.Add((result.Score, start, length, result.Reason));
                }
            }
        }

        if (scored.Count == 0)
        {
            return null;
        }

        var ordered = scored.OrderByDescending(s => s.Score).ThenBy(s => s.Length).ToList();
        var best = ordered[0];
        if (best.Score < RequiredPoints || (ordered.Count > 1 && ordered[1].Score == best.Score))
        {
            return null;
        }

        return new SeasonMatch(chain.Skip(best.Start).Take(best.Length).ToList(), best.Reason);
    }

    private static (int Score, string Reason)? Score(List<AniListSeason> span, int start, LibrarySeason season)
    {
        var first = span[0];
        var score = 0;
        var reasons = new List<string>();

        // Year, refined by month when both are known: two cours in one year must not be confused.
        if (season.Year is { } year && first.Year is { } entryYear)
        {
            var delta = Math.Abs(entryYear - year);
            if (delta >= 2)
            {
                return null;
            }

            var monthsApart = season.Month is { } month && first.Month is { } entryMonth
                ? Math.Abs(((entryYear * 12) + entryMonth) - ((year * 12) + month))
                : (int?)null;

            if (delta == 0 && monthsApart is null or <= 4)
            {
                score += YearPoints;
                reasons.Add(string.Create(CultureInfo.InvariantCulture, $"aired {entryYear}"));
            }
            else if (monthsApart is > 9)
            {
                return null;
            }
        }

        // Episodes: fewer files than aired is normal (a season still being collected); more is not.
        var known = span.All(entry => entry.Episodes is > 0);
        if (known && season.EpisodeCount > 0)
        {
            var aired = span.Sum(entry => entry.Episodes!.Value);
            if (season.EpisodeCount > aired + 1)
            {
                return null;
            }

            if (Math.Abs(aired - season.EpisodeCount) <= 1)
            {
                score += EpisodePoints;
                reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{aired} episodes"));
            }
        }

        if (start == season.Number - 1)
        {
            score += IndexPoints;
        }

        if (span.Count > 1)
        {
            score -= MergePenalty;
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{span.Count} parts in one season"));
        }

        var reason = reasons.Count == 0
            ? "sequel on AniList"
            : "sequel on AniList, " + string.Join(", ", reasons);
        return (score, reason);
    }
}
