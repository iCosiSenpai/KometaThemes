using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

#pragma warning disable CA1002, CA1305, CS1591, SA1513

namespace Jellyfin.Plugin.KometaThemes.Resolving;

public static class EpisodeRangeParser
{
    public static List<EpisodeRange> Parse(string? episodesString)
    {
        var ranges = new List<EpisodeRange>();

        if (string.IsNullOrWhiteSpace(episodesString))
        {
            return ranges;
        }

        var parts = episodesString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var match = Regex.Match(part, @"^(\d+)(?:-(\d+))?$");
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var start) || start < 0)
            {
                continue;
            }

            int? end = null;
            if (match.Groups[2].Success)
            {
                if (int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var e) && e >= start)
                {
                    end = e;
                }
            }

            ranges.Add(new EpisodeRange(start, end ?? start));
        }

        // Deduplicate while preserving order
        var seen = new HashSet<(int, int)>();
        var deduped = new List<EpisodeRange>();
        foreach (var r in ranges)
        {
            var key = (r.StartEpisode, r.EndEpisode);
            if (seen.Add(key))
            {
                deduped.Add(r);
            }
        }
        return deduped;
    }

    public static (int Start, int? End) GetBounds(List<EpisodeRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return (1, null);
        }

        var start = int.MaxValue;
        int? end = null;

        foreach (var range in ranges)
        {
            if (range.StartEpisode < start)
            {
                start = range.StartEpisode;
            }

            if (range.EndEpisode > (end ?? int.MinValue))
            {
                end = range.EndEpisode;
            }
        }

        return (start, end);
    }
}
