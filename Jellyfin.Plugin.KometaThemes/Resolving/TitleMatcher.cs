using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// What is known about a library item when it is matched by title.
/// </summary>
/// <param name="Titles">Its titles, strongest first: name, original title, folder name, slugs of other sites.</param>
/// <param name="WeakTitles">Pieces of those titles, such as <c>Paprika</c> from <c>Paprika - Sognando un sogno</c>.</param>
/// <param name="Years">Premiere years it may have: Jellyfin's, and one written in the folder name.</param>
/// <param name="TitleYear">A year written in the title itself to tell remakes apart, such as <c>Devil May Cry (2025)</c>.</param>
/// <param name="IsMovie">Whether the item is a movie.</param>
public sealed record TitleQuery(IReadOnlyList<string> Titles, IReadOnlyList<string> WeakTitles, IReadOnlyList<int> Years, int? TitleYear, bool IsMovie);

/// <summary>
/// Scores animethemes.moe entries against an item's titles. Pure: no network or disk.
/// </summary>
/// <remarks>
/// <para>
/// 2.0 compared the item only with the entry's main name and slug, word for word. Most anime are
/// known by other titles too: "The Café Terrace and Its Goddesses" is a synonym of "Megami no Café
/// Terrace", "DanMachi" one of "Dungeon ni Deai wo Motomeru…". Every title of an entry now counts,
/// and romanizations are folded (Jouzu and Jozu, Shoujo and Shojo, wo and o).
/// </para>
/// <para>
/// A title match is a guess, so it is checked against what else is known: the format (a movie is not
/// a TV series), the premiere year, and a season number in either title. A wrong song is worse than
/// none, so an uncertain match is left for the owner to pick.
/// </para>
/// </remarks>
public static partial class TitleMatcher
{
    /// <summary>Score of an exact title match.</summary>
    public const int Exact = 100;

    /// <summary>Lowest score shown as a suggestion in the manual search.</summary>
    public const int Suggestion = 60;

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "no", "tv", "ova", "oav", "ona", "movie", "film", "eiga", "gekijoban", "gekijouban", "specials", "special"
    };

    private static readonly Dictionary<string, int> OrdinalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7, ["eighth"] = 8,
    };

    private static readonly Dictionary<string, int> Romans = new(StringComparer.Ordinal)
    {
        ["II"] = 2, ["III"] = 3, ["IV"] = 4, ["V"] = 5, ["VI"] = 6, ["VII"] = 7, ["VIII"] = 8, ["IX"] = 9, ["X"] = 10,
    };

    /// <summary>
    /// Scores one entry against an item.
    /// </summary>
    /// <param name="query">The item.</param>
    /// <param name="anime">The entry, with its synonyms when known.</param>
    /// <returns>0 to 100.</returns>
    public static int Score(TitleQuery query, Anime anime)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(anime);

        var names = anime.Titles.Append(anime.Slug.Replace('_', ' ')).Select(Prepare).Where(t => t.Compact.Length > 0).ToList();
        var entryOrdinal = SeasonOrdinal(anime.Name) ?? SeasonOrdinal(anime.Slug.Replace('_', ' '));

        var best = 0.0;
        var bestOrdinal = (int?)null;
        foreach (var (title, weak) in query.Titles.Select(t => (t, false)).Concat(query.WeakTitles.Select(t => (t, true))))
        {
            var key = Prepare(title);
            if (key.Compact.Length < 2)
            {
                continue;
            }

            var similarity = names.Max(name => Similarity(key, name)) - (weak ? 0.1 : 0);
            if (similarity > best)
            {
                best = similarity;
                bestOrdinal = SeasonOrdinal(title);
            }
        }

        var score = best * 100;
        score -= OrdinalPenalty(bestOrdinal, entryOrdinal);
        score -= NumberPenalty(query, anime.Name);
        score -= FormatPenalty(query.IsMovie, anime.MediaFormat);
        score += YearAdjustment(query, anime.Year);
        return (int)Math.Clamp(Math.Round(score), 0, Exact);
    }

    /// <summary>
    /// Ranks entries against an item, best first; among equal scores the earliest entry of the right format wins.
    /// </summary>
    /// <param name="query">The item.</param>
    /// <param name="candidates">Entries.</param>
    /// <returns>Entries with their score.</returns>
    public static IReadOnlyList<(Anime Anime, int Score)> Rank(TitleQuery query, IEnumerable<Anime> candidates)
        => candidates
            .DistinctBy(anime => anime.Id)
            .Select(anime => (Anime: anime, Score: Score(query, anime)))
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => FormatPenalty(query.IsMovie, pair.Anime.MediaFormat))
            .ThenBy(pair => pair.Anime.Year ?? int.MaxValue)
            .ThenBy(pair => pair.Anime.Id)
            .ToList();

    /// <summary>
    /// Reads a season number from a title: "2nd Season", "Season 2", "Second Season", a final roman
    /// numeral ("DanMachi IV") or a final number ("Karakai Jouzu no Takagi-san 3").
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The number, or null when the title names none.</returns>
    public static int? SeasonOrdinal(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = NumberedSeasonRegex().Match(title);
        if (match.Success)
        {
            return int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        }

        match = SeasonNumberRegex().Match(title);
        if (match.Success)
        {
            return int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        }

        match = WordSeasonRegex().Match(title);
        if (match.Success && OrdinalWords.TryGetValue(match.Groups["w"].Value, out var word))
        {
            return word;
        }

        // A roman numeral or a small number closing the title, or the part before a subtitle.
        var head = title.Split(':', 2)[0].TrimEnd(' ', '.', '!', '?');
        match = TrailingTokenRegex().Match(head);
        if (match.Success)
        {
            var token = match.Groups["t"].Value;
            if (Romans.TryGetValue(token, out var roman))
            {
                return roman;
            }

            if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 2 and <= 12)
            {
                return number;
            }
        }

        return null;
    }

    /// <summary>
    /// Folds a title for comparison: no accents, no punctuation, lower case, one romanization.
    /// </summary>
    /// <param name="value">The title.</param>
    /// <returns>Its words, noise words included.</returns>
    internal static IReadOnlyList<string> Words(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var decomposed = value.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Romanize)
            .ToList();
    }

    private static string Romanize(string word)
    {
        if (word == "wo")
        {
            return "o";
        }

        // Long vowels are written ou, oo, uu or ō depending on who romanized the title.
        return word.Replace("ou", "o", StringComparison.Ordinal)
            .Replace("oo", "o", StringComparison.Ordinal)
            .Replace("uu", "u", StringComparison.Ordinal);
    }

    private static Prepared Prepare(string title)
    {
        var words = Words(title);
        var meaningful = words.Where(word => !NoiseWords.Contains(word)).ToList();
        if (meaningful.Count == 0)
        {
            meaningful = words.ToList();
        }

        return new Prepared(string.Concat(meaningful), meaningful);
    }

    private static double Similarity(Prepared key, Prepared name)
    {
        if (key.Compact == name.Compact)
        {
            return 1.0;
        }

        // Dice coefficient over words, where two long words a typo apart count as the same word.
        var used = new bool[name.Words.Count];
        var common = 0;
        foreach (var word in key.Words)
        {
            for (var i = 0; i < name.Words.Count; i++)
            {
                if (!used[i] && SameWord(word, name.Words[i]))
                {
                    used[i] = true;
                    common++;
                    break;
                }
            }
        }

        var dice = 2.0 * common / (key.Words.Count + name.Words.Count);

        // Whole-title similarity catches words split or joined differently ("Erandeiraremasen").
        var compact = Ratio(key.Compact, name.Compact);
        return Math.Max(dice, compact >= 0.9 ? compact : 0);
    }

    private static bool SameWord(string a, string b)
        => a == b || (a.Length >= 5 && b.Length >= 5 && Ratio(a, b) >= 0.8);

    private static int OrdinalPenalty(int? key, int? entry)
    {
        if (key == entry)
        {
            return 0;
        }

        // A plain title is the first season: an entry named "2nd Season" is another one.
        if (key == null)
        {
            return entry is null or 1 ? 0 : 20;
        }

        return entry == null ? (key == 1 ? 0 : 25) : 30;
    }

    /// <summary>
    /// Penalizes an entry whose name carries a part number none of the item's titles agrees with:
    /// "Camelot 2 Paladin; Agateram" is not "Camelot 1 - Wandering; Agateram".
    /// </summary>
    private static int NumberPenalty(TitleQuery query, string name)
    {
        var entry = Numbers(name);
        if (entry.Count == 0)
        {
            return 0;
        }

        return query.Titles.Any(title => Numbers(title) is { Count: > 0 } numbers && !numbers.Overlaps(entry)) ? 30 : 0;
    }

    private static HashSet<int> Numbers(string? title)
    {
        var numbers = new HashSet<int>();
        foreach (var token in (title ?? string.Empty).Split([' ', ':', '-', ';', ',', '.', '/', '(', ')', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Romans.TryGetValue(token, out var roman))
            {
                numbers.Add(roman);
            }
            else if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 20)
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    private static int FormatPenalty(bool isMovie, string? format)
    {
        // OVAs and specials sit between the two: some libraries file them as movies, some as series.
        if (format is "OVA" or "Special")
        {
            return 10;
        }

        var entryIsMovie = string.Equals(format, "Movie", StringComparison.OrdinalIgnoreCase);
        return isMovie == entryIsMovie ? 0 : 25;
    }

    private static double YearAdjustment(TitleQuery query, int? year)
    {
        if (year is not { } entryYear)
        {
            return 0;
        }

        // A year in the title is there to tell a remake from the original: it must agree.
        if (query.TitleYear is { } titleYear && Math.Abs(titleYear - entryYear) > 1)
        {
            return -40;
        }

        if (query.Years.Count == 0)
        {
            return 0;
        }

        var distance = query.Years.Min(y => Math.Abs(y - entryYear));
        return distance switch
        {
            0 => 4,
            1 => 2,
            <= 3 => 0,
            <= 6 => -8,
            _ => -30,
        };
    }

    /// <summary>
    /// Levenshtein similarity, 0 to 1.
    /// </summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <returns>1 for equal strings.</returns>
    internal static double Ratio(string a, string b)
    {
        if (a == b)
        {
            return 1;
        }

        var longest = Math.Max(a.Length, b.Length);
        if (longest == 0)
        {
            return 1;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return 1.0 - ((double)previous[b.Length] / longest);
    }

    [GeneratedRegex(@"\b(?<n>\d{1,2})(?:st|nd|rd|th)\s+(?:season|stagione)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberedSeasonRegex();

    [GeneratedRegex(@"\b(?:season|stagione|saison)\s*(?<n>\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonNumberRegex();

    [GeneratedRegex(@"\b(?<w>first|second|third|fourth|fifth|sixth|seventh|eighth)\s+season\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordSeasonRegex();

    [GeneratedRegex(@"\s(?<t>II|III|IV|V|VI|VII|VIII|IX|X|\d{1,2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingTokenRegex();

    private sealed record Prepared(string Compact, IReadOnlyList<string> Words);
}
