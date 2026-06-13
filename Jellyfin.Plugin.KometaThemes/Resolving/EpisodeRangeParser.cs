using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

#pragma warning disable CA1002, CA1305, CS1591

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

            var start = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int? end = match.Groups[2].Success
                ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                : null;

            ranges.Add(new EpisodeRange(start, end ?? start));
        }

        return ranges;
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
