using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
/// One anime of a franchise, as the season matcher sees it.
/// </summary>
/// <param name="Id">animethemes.moe anime ID.</param>
/// <param name="Name">Name.</param>
/// <param name="Year">Premiere year.</param>
/// <param name="Month">Premiere month: AniList's, else the first month of the animethemes.moe season.</param>
/// <param name="Format">animethemes.moe format: <c>TV</c>, <c>TV Short</c>, <c>ONA</c>, <c>OVA</c>, <c>Special</c>, <c>Movie</c>.</param>
/// <param name="Episodes">Episode count, when known.</param>
/// <param name="EpisodesExact">Whether <paramref name="Episodes"/> is the aired count (AniList) rather than the last episode a theme lists.</param>
/// <param name="Sequel">Whether AniList lists it among the sequels of the series.</param>
public sealed record FranchiseEntry(int Id, string Name, int? Year, int? Month, string? Format, int? Episodes, bool EpisodesExact, bool Sequel);

/// <summary>
/// The entries chosen for a season, and why.
/// </summary>
/// <param name="SeasonNumber">The season.</param>
/// <param name="EntryIds">Entries, in order; empty when the season plays the series themes.</param>
/// <param name="Window">The episodes of the single entry this season holds, when seasons share an entry.</param>
/// <param name="Reason">Plain-language explanation shown to the owner.</param>
public sealed record SeasonMatch(int SeasonNumber, IReadOnlyList<int> EntryIds, EpisodeWindow? Window, string Reason)
{
    /// <summary>Gets a value indicating whether the season has entries of its own.</summary>
    public bool Matched => EntryIds.Count > 0;
}

/// <summary>
/// Matches the seasons of a series to the anime of its franchise. Pure: no network or disk.
/// </summary>
/// <remarks>
/// <para>
/// Later seasons are separate anime on animethemes.moe. 1.x split the themes of one anime by episode
/// and handed the second season the first season's second ending; 2.0 followed AniList's TV sequels
/// one season at a time, which stopped at the first movie (Sword Art Online never reached
/// Alicization) and stopped entirely when AniList switched its API off.
/// </para>
/// <para>
/// The seasons are now aligned with the franchise as a whole, in order: season numbers grow with
/// premiere dates, so a season can only take an entry after the one the previous season took. Each
/// pairing is scored by premiere date, episode count, format, a season number in the entry's name and
/// AniList's sequel list when it is available. A season can take up to three consecutive entries (a
/// split cour that TVDB counts as one season), and consecutive seasons can share one long entry, each
/// keeping the themes of its own episodes. A pairing needs two signals that agree and must beat every
/// alternative; otherwise the season plays the series themes, which is never the wrong song.
/// </para>
/// </remarks>
public static partial class SeasonMatcher
{
    /// <summary>Lowest score a pairing needs.</summary>
    internal const int Required = 4;

    private const int MaxMergedEntries = 3;
    private const int Veto = int.MinValue / 4;

    private static readonly string[] SeasonFormats = ["TV", "TV Short", "ONA"];

    /// <summary>
    /// Aligns the seasons of a series with its franchise.
    /// </summary>
    /// <param name="seasons">Seasons in the library, numbered 1 or higher.</param>
    /// <param name="entries">Entries of the franchise, the series' own entry included.</param>
    /// <param name="rootId">The entry the series itself is matched to.</param>
    /// <returns>One result per season, in season order.</returns>
    public static IReadOnlyList<SeasonMatch> Align(IReadOnlyList<LibrarySeason> seasons, IReadOnlyList<FranchiseEntry> entries, int rootId)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        ArgumentNullException.ThrowIfNull(entries);

        var ordered = seasons.Where(s => s.Number >= 1).OrderBy(s => s.Number).ToList();
        var franchise = entries
            .DistinctBy(e => e.Id)
            .OrderBy(e => e.Year ?? int.MaxValue)
            .ThenBy(e => e.Month ?? 13)
            .ThenBy(e => e.Id)
            .ToList();

        var solver = new Solver(ordered, franchise, rootId);
        var plan = solver.Solve();
        var results = new List<SeasonMatch>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var step = plan[i];
            var season = ordered[i];
            if (step == null)
            {
                results.Add(new SeasonMatch(season.Number, [], null, InheritReason(season, franchise.Count)));
                continue;
            }

            if (!solver.IsUnique(plan, i))
            {
                results.Add(new SeasonMatch(season.Number, [], null, "plays the series themes: more than one entry fits it equally well"));
                continue;
            }

            results.Add(new SeasonMatch(season.Number, step.Entries.Select(e => e.Id).ToList(), step.Window, step.Reason));
        }

        return results;
    }

    private static string InheritReason(LibrarySeason season, int entries)
        => entries <= 1
            ? "plays the series themes: animethemes.moe lists no other entry of this franchise"
            : season.Year == null && season.EpisodeCount == 0
                ? "plays the series themes: Jellyfin knows neither its premiere nor its episodes"
                : "plays the series themes: no entry of the franchise matches its premiere and episodes";

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:gaiden|side story|spin-?off|alternative|picture drama|recap|chibi|mini|petit|puchi)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SideStoryRegex();

    private static bool IsSeasonFormat(string? format)
        => format == null || SeasonFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    /// <summary>One season's pairing in a plan.</summary>
    private sealed record Step(IReadOnlyList<FranchiseEntry> Entries, int Start, int End, EpisodeWindow? Window, int Score, string Reason);

    private sealed class Solver
    {
        private readonly IReadOnlyList<LibrarySeason> _seasons;
        private readonly IReadOnlyList<FranchiseEntry> _entries;
        private readonly int _rootId;
        private readonly string _rootName;
        private readonly Dictionary<(int Season, int Next, int Shared, int Used, bool Previous), (int Score, Step?[] Plan)> _memo = [];

        public Solver(IReadOnlyList<LibrarySeason> seasons, IReadOnlyList<FranchiseEntry> entries, int rootId)
        {
            _seasons = seasons;
            _entries = entries;
            _rootId = rootId;
            _rootName = entries.FirstOrDefault(e => e.Id == rootId)?.Name ?? string.Empty;
        }

        public Step?[] Solve() => Best(0, 0, -1, 0, false).Plan;

        /// <summary>
        /// Tells whether a season's pairing beats every other pairing that fits between its neighbours.
        /// </summary>
        public bool IsUnique(Step?[] plan, int index)
        {
            var chosen = plan[index]!;
            var from = 0;
            for (var i = index - 1; i >= 0; i--)
            {
                if (plan[i] is { } before)
                {
                    from = before.End;
                    break;
                }
            }

            var to = _entries.Count;
            for (var i = index + 1; i < plan.Length; i++)
            {
                if (plan[i] is { } after)
                {
                    to = after.Window == null ? after.Start : after.Start + 1;
                    break;
                }
            }

            var previousMatched = index > 0 && plan[index - 1] != null;
            foreach (var run in Runs(from, to))
            {
                if (run.Select(i => _entries[i].Id).SequenceEqual(chosen.Entries.Select(e => e.Id)))
                {
                    continue;
                }

                var alternative = Score(_seasons[index], index, run, previousMatched ? from : -1);
                if (alternative.Score >= Required && alternative.Score >= chosen.Score)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Lists the entries a season could take: one entry, or a first entry followed by up to two
        /// later parts. Parts skip what lies between them that cannot be a part (a recap, a movie, a
        /// side story, an entry with no episodes listed), and follow one another within 15 months.
        /// </summary>
        private IEnumerable<int[]> Runs(int from, int to)
        {
            for (var start = from; start < to; start++)
            {
                var run = new List<int> { start };
                yield return [.. run];
                for (var next = start + 1; next < to && run.Count < MaxMergedEntries; next++)
                {
                    var part = _entries[next];
                    if (!IsSeasonFormat(part.Format) || IsSideStory(part) || part.Episodes is not > 0)
                    {
                        continue;
                    }

                    if (MonthsApart(_entries[run[^1]], part) is > 15)
                    {
                        break;
                    }

                    run.Add(next);
                    yield return [.. run];
                }
            }
        }

        /// <param name="i">Season to place.</param>
        /// <param name="next">First entry it may take.</param>
        /// <param name="shared">Entry the previous season took alone, which this one may continue; -1 for none.</param>
        /// <param name="used">Episodes of <paramref name="shared"/> the previous seasons hold.</param>
        /// <param name="previous">Whether the previous season took entries.</param>
        private (int Score, Step?[] Plan) Best(int i, int next, int shared, int used, bool previous)
        {
            if (i == _seasons.Count)
            {
                return (0, new Step?[_seasons.Count]);
            }

            var key = (i, next, shared, used, previous);
            if (_memo.TryGetValue(key, out var known))
            {
                return known;
            }

            // Playing the series themes is always possible.
            var rest = Best(i + 1, next, -1, 0, false);
            var best = (rest.Score, Plan: With(rest.Plan, i, null));

            var season = _seasons[i];
            if (shared >= 0 && Continue(season, _entries[shared], used) is { } continued)
            {
                var after = Best(i + 1, next, shared, used + season.EpisodeCount, true);
                if (continued.Score + after.Score > best.Score)
                {
                    best = (continued.Score + after.Score, With(after.Plan, i, continued with { Start = shared, End = shared + 1 }));
                }
            }

            foreach (var run in Runs(next, _entries.Count))
            {
                var step = Score(season, i, run, previous ? next : -1);
                if (step.Score < Required)
                {
                    continue;
                }

                var single = run.Length == 1;
                var after = Best(i + 1, step.End, single ? run[0] : -1, single ? season.EpisodeCount : 0, true);
                if (step.Score + after.Score > best.Score)
                {
                    best = (step.Score + after.Score, With(after.Plan, i, step));
                }
            }

            _memo[key] = best;
            return best;
        }

        private static Step?[] With(Step?[] plan, int index, Step? step)
        {
            var copy = (Step?[])plan.Clone();
            copy[index] = step;
            return copy;
        }

        /// <summary>
        /// Scores a season taking some entries.
        /// </summary>
        /// <param name="season">The season.</param>
        /// <param name="index">Its position among the library seasons.</param>
        /// <param name="indices">The entries, from <see cref="Runs"/>.</param>
        /// <param name="afterMatched">First entry free after the previous season's entries, when that season took any; else -1.</param>
        private Step Score(LibrarySeason season, int index, int[] indices, int afterMatched)
        {
            var run = indices.Select(i => _entries[i]).ToList();
            var first = run[0];
            var start = indices[0];
            var length = indices[^1] - start + 1;
            var score = 0;
            var reasons = new List<string>();

            // Premiere: the strongest signal. A season far from the entry's premiere is never that entry.
            if (season.Year is { } year && first.Year is { } entryYear)
            {
                if (season.Month is { } month && first.Month is { } entryMonth)
                {
                    var apart = Math.Abs(((entryYear * 12) + entryMonth) - ((year * 12) + month));
                    if (apart > 14)
                    {
                        return Rejected(start, length);
                    }

                    var points = apart <= 2 ? 3 : apart <= 5 ? 2 : apart <= 9 ? 1 : 0;
                    score += points;
                    if (points > 0)
                    {
                        reasons.Add(Premiere(first));
                    }
                }
                else
                {
                    var apart = Math.Abs(entryYear - year);
                    if (apart > 1)
                    {
                        return Rejected(start, length);
                    }

                    if (apart == 0)
                    {
                        score += 2;
                        reasons.Add(Premiere(first));
                    }
                }
            }

            // Episodes: fewer in the library than aired is normal (a season still being collected);
            // more is not. Counts read from theme lists are a lower bound: the last episode often has no ending.
            if (season.EpisodeCount > 0 && run.All(e => e.Episodes is > 0))
            {
                var aired = run.Sum(e => e.Episodes!.Value);
                var exact = run.All(e => e.EpisodesExact);
                var have = season.EpisodeCount;
                if (exact)
                {
                    if (have > aired + 1)
                    {
                        return Rejected(start, length);
                    }

                    if (Math.Abs(aired - have) <= 1)
                    {
                        score += 3;
                        reasons.Add(Episodes(aired));
                    }
                }
                else if (have >= aired - 1 && have <= aired + 2)
                {
                    // A lower bound proves nothing when it is far below: themes are often listed only
                    // for the first episodes they air in.
                    score += 2;
                    reasons.Add(Episodes(have));
                }
            }

            // Format: seasons are TV, TV Short or ONA; an OVA, a special or a movie only with strong evidence.
            score -= first.Format switch
            {
                "OVA" or "Special" => 1,
                "Movie" => 3,
                _ => 0,
            };

            // A side story shares the franchise and often the format, but it is not the next season.
            if (IsSideStory(first))
            {
                score -= 3;
            }

            // A season number written in the entry's name, after the part it shares with the series'
            // own entry: the III of "Lupin III: Part 5" is not a season.
            if (TitleMatcher.SeasonOrdinal(AfterCommonPrefix(first.Name, _rootName)) is { } ordinal)
            {
                if (ordinal == season.Number)
                {
                    score += 2;
                    reasons.Add(string.Create(CultureInfo.InvariantCulture, $"named season {ordinal}"));
                }
                else
                {
                    score -= 1;
                }
            }

            if (run.All(e => e.Sequel || e.Id == _rootId))
            {
                score += 1;
                if (first.Id != _rootId)
                {
                    reasons.Add("sequel on AniList");
                }
            }

            // The series' own entry belongs to its first season in the library.
            if (first.Id == _rootId)
            {
                if (index != 0)
                {
                    return Rejected(start, length);
                }

                score += 2;
            }

            // The entry right after the previous season's: seasons come in order.
            if (afterMatched >= 0 && start == NextSeasonLike(afterMatched))
            {
                score += 2;
                reasons.Add("next in the franchise");
            }

            score -= run.Count - 1;
            if (run.Count > 1)
            {
                reasons.Add(string.Create(CultureInfo.InvariantCulture, $"{run.Count} parts in one season"));
            }

            var reason = reasons.Count == 0 ? "the series' own entry" : string.Join(", ", reasons);
            return new Step(run, start, start + length, null, score, reason);
        }

        /// <summary>
        /// Scores a season continuing the long entry the previous season took.
        /// </summary>
        private Step? Continue(LibrarySeason season, FranchiseEntry entry, int used)
        {
            if (season.EpisodeCount == 0 || entry.Episodes is not { } aired || !IsSeasonFormat(entry.Format))
            {
                return null;
            }

            // The entry must have aired these episodes; a count read from theme lists must reach them.
            var last = used + season.EpisodeCount;
            if (last > aired + (entry.EpisodesExact ? 1 : 0))
            {
                return null;
            }

            // And the season must have started while the entry was airing (about four episodes a month).
            if (season.Year is { } year && entry.Year is { } entryYear)
            {
                var airedMonths = (aired / 4) + 3;
                var start = (entryYear * 12) + (entry.Month ?? 1);
                var at = (year * 12) + (season.Month ?? 6);
                if (at < start || at > start + airedMonths)
                {
                    return null;
                }
            }

            var window = new EpisodeWindow(used + 1, last);
            var reason = string.Create(CultureInfo.InvariantCulture, $"episodes {window.First}–{window.Last} of the same entry");
            return new Step([entry], 0, 0, window, Required, reason);
        }

        /// <summary>
        /// Drops the words an entry's name shares with the series' own entry: "Lupin III: Part 5"
        /// against "Lupin III: Mine Fujiko to Iu Onna" leaves " Part 5". The space before the rest is
        /// kept, so a final " II" still reads as a numeral.
        /// </summary>
        private static string AfterCommonPrefix(string name, string root)
        {
            var common = 0;
            while (common < name.Length && common < root.Length && char.ToUpperInvariant(name[common]) == char.ToUpperInvariant(root[common]))
            {
                common++;
            }

            if (common == name.Length)
            {
                return string.Empty;
            }

            // Only whole words count: back off to the space that starts the first differing word.
            var space = name.LastIndexOf(' ', Math.Max(0, common - 1));
            if (name[common] == ' ')
            {
                space = common;
            }

            return space <= 0 ? name : name[space..];
        }

        private bool IsSideStory(FranchiseEntry entry)
            => SideStoryRegex().IsMatch(AfterCommonPrefix(entry.Name, _rootName));

        private int NextSeasonLike(int from)
        {
            for (var i = from; i < _entries.Count; i++)
            {
                if (IsSeasonFormat(_entries[i].Format) && !IsSideStory(_entries[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static Step Rejected(int start, int length) => new([], start, start + length, null, Veto, string.Empty);

        private static int? MonthsApart(FranchiseEntry a, FranchiseEntry b)
            => a.Year is { } ya && b.Year is { } yb ? Math.Abs(((yb * 12) + (b.Month ?? 1)) - ((ya * 12) + (a.Month ?? 1))) : null;

        private static string Premiere(FranchiseEntry entry)
            => entry.Month is { } month
                ? string.Create(CultureInfo.InvariantCulture, $"premiered {CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month)} {entry.Year}")
                : string.Create(CultureInfo.InvariantCulture, $"premiered {entry.Year}");

        private static string Episodes(int count) => string.Create(CultureInfo.InvariantCulture, $"{count} episodes");
    }
}
