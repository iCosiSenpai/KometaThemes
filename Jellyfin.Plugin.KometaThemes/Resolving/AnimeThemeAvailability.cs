using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Checks whether an anime entry has anything to download.
/// </summary>
internal static class AnimeThemeAvailability
{
    /// <summary>
    /// Tells whether an anime has at least one opening or ending with a downloadable file.
    /// </summary>
    /// <param name="anime">The anime.</param>
    /// <returns>True when something can be downloaded.</returns>
    public static bool HasUsableTheme(Anime anime)
        => (anime.Themes ?? []).Any(theme =>
            theme.Type is ThemeType.OP or ThemeType.ED
            && (theme.Entries ?? []).Any(entry =>
                (entry.Videos ?? []).Any(video => !string.IsNullOrWhiteSpace(video.Link) && !string.IsNullOrWhiteSpace(video.Audio?.Link))));
}
