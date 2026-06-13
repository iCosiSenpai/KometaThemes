using System;

#pragma warning disable SA1516, CS1591

namespace Jellyfin.Plugin.KometaThemes.Resolving;

public sealed class EpisodeRange
{
    public EpisodeRange(int startEpisode, int? endEpisode)
    {
        StartEpisode = startEpisode;
        EndEpisode = endEpisode ?? startEpisode;
    }

    public int StartEpisode { get; }
    public int EndEpisode { get; }

    public bool Contains(int episode) =>
        episode >= StartEpisode && episode <= EndEpisode;

    public bool Overlaps(EpisodeRange other) =>
        StartEpisode <= other.EndEpisode && EndEpisode >= other.StartEpisode;
}
