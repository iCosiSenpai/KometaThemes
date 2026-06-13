using System;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.Logging;

#pragma warning disable SA1611, SA1615, CS1591

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Detects which season a Jellyfin item represents.
/// Supports name-based detection (e.g. "Season 2", "2nd Season", "2ª Stagione")
/// and episode-range-based detection.
/// </summary>
public class SeasonDetector
{
    private readonly ILogger<SeasonDetector> _logger;

    private static readonly (string Pattern, int Group)[] SeasonPatterns =
    [
        ("\\b[Ss]eason\\s*(\\d+)\\b", 1),
        ("(\\d+)(?:st|nd|rd|th)\\s+[Ss]eason\\b", 1),
        ("\\bS(\\d+)\\b", 1),
        ("(\\d+)[ªa]\\s+[Ss]tagione\\b", 1),
        ("(\\d+)°\\s*[Ss]tagione\\b", 1),
        ("[Pp]art\\s*(\\d+)", 1),
        ("[Pp]art\\s+(IX|IV|V?I{0,3})$", 0),
        ("\\s+(IX|IV|V?I{1,3}|VI{0,3})$", 0),
        (":[^:]*?[Ss]eason\\s*(\\d+)", 1),
        ("\\s+(\\d+)\\s*$", 1),
        ("\\([Ss]eason\\s*(\\d+)\\)", 1),
    ];

    private static readonly Regex[] CompiledSeasonRegexes =
        SeasonPatterns.Select(p => new Regex(p.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)).ToArray();

    public SeasonDetector(ILogger<SeasonDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects the season number for a Jellyfin item based on the configured mode.
    /// </summary>
    /// <param name="item">The Jellyfin item (Series or Season).</param>
    /// <param name="mode">Detection mode: "ByName", "ByEpisodeRange", "Auto".</param>
    /// <returns>Detected season number (1-based), or null if unknown.</returns>
    public int? DetectSeason(BaseItem item, string? mode = "Auto")
    {
        if (item is Season season)
        {
            return DetectFromSeason(season, mode);
        }

        if (item is Series series)
        {
            return DetectFromSeries(series, mode);
        }

        return null;
    }

    private int? DetectFromSeason(Season season, string? mode)
    {
        if (season.IndexNumber.HasValue && mode != "ByName")
        {
            _logger.LogDebug("Season {Name} has IndexNumber={Num}", season.Name, season.IndexNumber.Value);
            return season.IndexNumber.Value;
        }

        if (mode != "ByEpisodeRange")
        {
            return DetectFromName(season.Name);
        }

        return null;
    }

    private int? DetectFromSeries(Series series, string? mode)
    {
        if (series.IndexNumber.HasValue && mode != "ByName")
        {
            _logger.LogDebug("Series {Name} has IndexNumber={Num}", series.Name, series.IndexNumber.Value);
            return series.IndexNumber.Value;
        }

        if (mode != "ByEpisodeRange")
        {
            int? nameResult = DetectFromName(series.Name);
            if (nameResult.HasValue)
            {
                return nameResult;
            }

            if (!string.IsNullOrWhiteSpace(series.OriginalTitle))
            {
                return DetectFromName(series.OriginalTitle);
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a season number from a name using regex patterns.
    /// Supports EN, IT, JP, and common anime naming conventions.
    /// </summary>
    public static int? DetectFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (var i = 0; i < SeasonPatterns.Length; i++)
        {
            var (_, group) = SeasonPatterns[i];
            var regex = CompiledSeasonRegexes[i];
            var match = regex.Match(name);
            if (!match.Success)
            {
                continue;
            }

            if (group == 0)
            {
                var roman = match.Groups[1].Value.ToUpperInvariant();
                var num = RomanToInt(roman);
                if (num.HasValue)
                {
                    return num.Value;
                }
            }
            else
            {
                if (int.TryParse(match.Groups[group].Value, out var num) && num > 0 && num <= 50)
                {
                    return num;
                }
            }
        }

        return null;
    }

    private static int? RomanToInt(string roman)
    {
        return roman switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            "VIII" => 8,
            "IX" => 9,
            "X" => 10,
            "XI" => 11,
            "XII" => 12,
            _ => null
        };
    }
}
