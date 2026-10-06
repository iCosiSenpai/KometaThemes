using System;
using System.Collections.Generic;
using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Resolving;

/// <summary>
/// Outcome of looking up an item's anime.
/// </summary>
public enum MatchState
{
    /// <summary>One or more anime entries were found.</summary>
    Matched,

    /// <summary>A season with no entry of its own: it plays the series themes.</summary>
    Inherit,

    /// <summary>animethemes.moe has no entry for the item.</summary>
    NotFound,

    /// <summary>The lookup could not be made (network, outage). Nothing is changed on disk.</summary>
    Unavailable
}

/// <summary>
/// The anime entries an item maps to, and how they were found.
/// </summary>
/// <param name="State">Outcome.</param>
/// <param name="Animes">Matched entries; several only for a season that spans more than one.</param>
/// <param name="Method">How the match was made, in words the owner understands.</param>
/// <param name="Manual">Whether the owner chose the match.</param>
public sealed record Resolution(MatchState State, IReadOnlyList<Anime> Animes, string Method, bool Manual)
{
    /// <summary>
    /// Creates a match.
    /// </summary>
    /// <param name="animes">Matched entries.</param>
    /// <param name="method">How they were found.</param>
    /// <param name="manual">Whether the owner chose them.</param>
    /// <returns>The resolution.</returns>
    public static Resolution Found(IReadOnlyList<Anime> animes, string method, bool manual = false)
        => new(MatchState.Matched, animes, method, manual);

    /// <summary>
    /// Creates a result for an item without a match.
    /// </summary>
    /// <param name="state">Why there is no match.</param>
    /// <param name="method">Explanation.</param>
    /// <returns>The resolution.</returns>
    public static Resolution Without(MatchState state, string method)
    {
        if (state == MatchState.Matched)
        {
            throw new ArgumentException("A resolution without anime cannot be a match.", nameof(state));
        }

        return new(state, [], method, false);
    }
}
