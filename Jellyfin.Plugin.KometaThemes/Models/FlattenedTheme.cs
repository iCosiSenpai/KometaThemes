namespace Jellyfin.Plugin.KometaThemes.Models;

#pragma warning disable CS1591

public sealed record FlattenedTheme(AnimeTheme Theme, AnimeThemeEntry Entry, Video Video, Audio Audio);
