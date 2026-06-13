using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Helpers for checking whether an AnimeThemes match contains usable media.
/// </summary>
internal static class AnimeThemeAvailability
{
    /// <summary>
    /// Returns true when at least one anime contains a theme entry with a downloadable audio or video link.
    /// </summary>
    /// <param name="anime">Anime matches to inspect.</param>
    /// <returns>Whether any match has usable theme media.</returns>
    public static bool HasAnyUsableTheme(Anime[] anime)
    {
        return anime.Any(HasUsableTheme);
    }

    /// <summary>
    /// Returns true when the anime contains a theme entry with a downloadable audio or video link.
    /// </summary>
    /// <param name="anime">Anime match to inspect.</param>
    /// <returns>Whether the match has usable theme media.</returns>
    public static bool HasUsableTheme(Anime anime)
    {
        return anime.Themes?.Any(theme =>
            theme.Entries?.Any(entry =>
                entry.Videos?.Any(video =>
                    !string.IsNullOrWhiteSpace(video.Link) ||
                    !string.IsNullOrWhiteSpace(video.Audio?.Link)) == true) == true) == true;
    }
}
