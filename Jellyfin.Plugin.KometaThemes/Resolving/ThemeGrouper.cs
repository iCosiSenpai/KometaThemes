using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1002, SA1611, SA1615, CA1305, CS1591

namespace Jellyfin.Plugin.KometaThemes.Resolving;

public class ThemeGrouper
{
    private readonly ILogger<ThemeGrouper> _logger;

    public ThemeGrouper(ILogger<ThemeGrouper> logger)
    {
        _logger = logger;
    }

    public List<SeasonGroup> GroupThemesBySeason(IEnumerable<FlattenedTheme> themes)
    {
        var themeList = themes.ToList();
        var groups = new List<SeasonGroup>();

        var themesWithRanges = themeList
            .Select(t => (Theme: t, Ranges: EpisodeRangeParser.Parse(t.Entry.Episodes)))
            .ToList();

        var allRanges = themesWithRanges
            .SelectMany(t => t.Ranges)
            .Select(r => (r.StartEpisode, r.EndEpisode))
            .Distinct()
            .OrderBy(r => r.StartEpisode)
            .ToList();

        if (allRanges.Count == 0)
        {
            groups.Add(new SeasonGroup(1, null, null, new Collection<FlattenedTheme>(themeList)));
            _logger.LogDebug("No episode ranges found, all {Count} themes assigned to season 1", themeList.Count);
            return groups;
        }

        var seasonNumber = 1;
        var usedRanges = new HashSet<(int, int)>();

        foreach (var range in allRanges)
        {
            var key = (range.StartEpisode, range.EndEpisode);
            if (usedRanges.Contains(key))
            {
                continue;
            }

            usedRanges.Add(key);

            var matchingThemes = themesWithRanges
                .Where(t => t.Ranges.Any(r => r.StartEpisode == range.StartEpisode && r.EndEpisode == range.EndEpisode))
                .Select(t => t.Theme)
                .ToList();

            if (matchingThemes.Count > 0)
            {
                groups.Add(new SeasonGroup(seasonNumber, range.StartEpisode, range.EndEpisode, new Collection<FlattenedTheme>(matchingThemes)));
                _logger.LogDebug(
                    "Season {Num}: episodes {Start}-{End}, {Count} themes",
                    seasonNumber,
                    range.StartEpisode,
                    range.EndEpisode.ToString(),
                    matchingThemes.Count);
                seasonNumber++;
            }
        }

        var unmatched = themesWithRanges
            .Where(t => t.Ranges.Count == 0)
            .Select(t => t.Theme)
            .ToList();

        if (unmatched.Count > 0)
        {
            var lastSeason = groups.Count > 0 ? groups[^1].SeasonNumber + 1 : 1;
            groups.Add(new SeasonGroup(lastSeason, null, null, new Collection<FlattenedTheme>(unmatched)));
            _logger.LogDebug("Season {Num}: no episode range, {Count} unmatched themes", lastSeason, unmatched.Count);
        }

        return groups;
    }

    public SeasonGroup? FindMatchingGroup(List<SeasonGroup> groups, int? seasonNumber)
    {
        if (groups.Count == 0)
        {
            return null;
        }

        if (seasonNumber.HasValue)
        {
            var match = groups.FirstOrDefault(g => g.SeasonNumber == seasonNumber.Value);
            if (match != null)
            {
                _logger.LogDebug("Found exact season match: Season {Num}", seasonNumber.Value);
                return match;
            }
        }

        _logger.LogDebug("No exact season match found, returning first group (Season {Num})", groups[0].SeasonNumber);
        return groups[0];
    }
}
