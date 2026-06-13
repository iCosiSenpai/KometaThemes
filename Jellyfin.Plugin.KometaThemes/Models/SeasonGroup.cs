using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.KometaThemes.Models;

#pragma warning disable CA1002, CS1591

public sealed record SeasonGroup(
    int SeasonNumber,
    int? StartEpisode,
    int? EndEpisode,
    Collection<FlattenedTheme> Themes
);

#pragma warning restore CA1002
