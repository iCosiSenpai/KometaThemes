namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Episodes a theme version is listed for, such as 2 to 12.
/// </summary>
/// <param name="StartEpisode">First episode.</param>
/// <param name="EndEpisode">Last episode, the first when the range is a single episode.</param>
public sealed record EpisodeRange(int StartEpisode, int EndEpisode);

/// <summary>
/// The episodes of one anime entry that a library season holds, when several seasons share the
/// entry: TVDB splits Black Clover's 170 episodes into four seasons, animethemes.moe lists one anime.
/// </summary>
/// <param name="First">First episode of the entry in the season.</param>
/// <param name="Last">Last episode of the entry in the season.</param>
public sealed record EpisodeWindow(int First, int Last);
