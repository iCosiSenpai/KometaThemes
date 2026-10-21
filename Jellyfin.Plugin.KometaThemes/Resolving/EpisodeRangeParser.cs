using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Reads the episode lists animethemes.moe writes, such as <c>1-12</c> or <c>2, 4-12</c>.
/// </summary>
public static partial class EpisodeRangeParser
{
    /// <summary>
    /// Parses an episode list. Parts that are not a number or a range are skipped.
    /// </summary>
    /// <param name="episodes">The list.</param>
    /// <returns>The ranges, in their written order, without duplicates.</returns>
    public static IReadOnlyList<EpisodeRange> Parse(string? episodes)
    {
        if (string.IsNullOrWhiteSpace(episodes))
        {
            return [];
        }

        var ranges = new List<EpisodeRange>();
        foreach (var part in episodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = RangeRegex().Match(part);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
            {
                continue;
            }

            var end = start;
            if (match.Groups[2].Success
                && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var last)
                && last >= start)
            {
                end = last;
            }

            ranges.Add(new EpisodeRange(start, end));
        }

        return ranges.Distinct().ToList();
    }

    [GeneratedRegex(@"^(\d{1,4})(?:\s*-\s*(\d{1,4}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex RangeRegex();
}
