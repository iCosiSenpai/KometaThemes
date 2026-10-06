using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// File names of downloaded themes.
/// </summary>
/// <remarks>
/// 1.x named files after the theme slug, title-cased, with the volume as a suffix:
/// <c>OP0 - Op1__50.mp3</c>, where <c>0</c> stood for a missing sequence and <c>Op1</c> repeated the
/// code. 2.0 uses the song: <c>OP1 - Love Dramatic.mp3</c>. The volume now lives in the folder's state
/// file, and existing files are renamed rather than downloaded again.
/// </remarks>
public static partial class ThemeNaming
{
    /// <summary>Longest song title kept in a file name.</summary>
    public const int MaxTitleLength = 80;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Gets the short code of a theme: its slug when it is a proper code, else type and sequence.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <returns>A code such as <c>OP1</c>, <c>ED2</c> or <c>ED1-TV</c>.</returns>
    public static string Code(AnimeTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var slug = theme.Slug?.Trim();
        if (!string.IsNullOrEmpty(slug) && CodeRegex().IsMatch(slug))
        {
            return slug.ToUpperInvariant();
        }

        var type = theme.Type == ThemeType.ED ? "ED" : "OP";
        return string.Create(CultureInfo.InvariantCulture, $"{type}{theme.Sequence ?? 1}");
    }

    /// <summary>
    /// Builds the file name for a theme.
    /// </summary>
    /// <param name="code">Theme code.</param>
    /// <param name="title">Song title, if known.</param>
    /// <param name="extension">Extension with the dot, such as <c>.mp3</c>.</param>
    /// <returns>A safe file name.</returns>
    public static string FileName(string code, string? title, string extension)
    {
        var safeCode = Sanitize(code);
        if (safeCode.Length == 0)
        {
            safeCode = "Theme";
        }

        var safeTitle = Sanitize(title);
        if (safeTitle.Length > MaxTitleLength)
        {
            safeTitle = safeTitle[..MaxTitleLength].TrimEnd(' ', '.', '-');
        }

        var name = safeTitle.Length == 0 ? safeCode : safeCode + " - " + safeTitle;
        if (ReservedNames.Contains(name))
        {
            name = "_" + name;
        }

        return name + extension;
    }

    /// <summary>
    /// Makes a name unique against names already taken in a folder.
    /// </summary>
    /// <param name="fileName">Desired name.</param>
    /// <param name="taken">Names already used, compared case-insensitively.</param>
    /// <returns>The name, or the name with a number appended.</returns>
    public static string Unique(string fileName, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(taken);
        if (!taken.Contains(fileName))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({n}){extension}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Reads the volume a 1.x file name carries, such as 50 for <c>OP1 - Op1__50.mp3</c>.
    /// </summary>
    /// <param name="fileName">File name.</param>
    /// <returns>The volume percentage, or null.</returns>
    public static int? LegacyVolume(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var match = LegacyVolumeRegex().Match(Path.GetFileNameWithoutExtension(fileName));
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var volume) && volume <= 100
            ? volume
            : null;
    }

    /// <summary>
    /// Tells whether a name is a bare file name that cannot leave its folder.
    /// </summary>
    /// <param name="fileName">Name to check.</param>
    /// <returns>True when safe.</returns>
    public static bool IsPlainFileName(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName)
            && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            && fileName != "."
            && fileName != ".."
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormC))
        {
            if (char.IsControl(c))
            {
                continue;
            }

            builder.Append(invalid.Contains(c) ? ' ' : c);
        }

        var collapsed = SpacesRegex().Replace(builder.ToString(), " ").Trim();
        return collapsed.Trim('.', ' ');
    }

    [GeneratedRegex(@"^(OP|ED)\d*(-[A-Za-z0-9]+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"__(\d{1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyVolumeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();
}
