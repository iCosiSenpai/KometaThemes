using Jellyfin.Plugin.KometaThemes.Models;

namespace Jellyfin.Plugin.KometaThemes.Configuration;

/// <summary>
/// What to download for one media type (theme songs or theme videos).
/// </summary>
/// <remarks>
/// The property names are the ones 1.x wrote to the configuration file, so an upgrade keeps them.
/// </remarks>
public class MediaTypeConfiguration
{
    /// <summary>Gets or sets how many themes to download.</summary>
    public FetchType FetchType { get; set; }

    /// <summary>Gets or sets a value indicating whether versions with episode content over the theme are skipped.</summary>
    public bool IgnoreOverlapping { get; set; }

    /// <summary>Gets or sets a value indicating whether endings are skipped.</summary>
    public bool IgnoreEDs { get; set; }

    /// <summary>Gets or sets a value indicating whether openings are skipped.</summary>
    public bool IgnoreOPs { get; set; }

    /// <summary>Gets or sets a value indicating whether only creditless versions are accepted.</summary>
    public bool IgnoreThemesWithCredits { get; set; }

    /// <summary>Gets or sets the volume baked into the file, 0 to 1.</summary>
    public double Volume { get; set; }

    /// <summary>Gets a value indicating whether this media type is downloaded at all.</summary>
    public bool Enabled => FetchType != FetchType.None && !(IgnoreOPs && IgnoreEDs);

    /// <summary>Gets the volume as a whole percentage, the unit stored with each file.</summary>
    public int VolumePercent => (int)System.Math.Round(System.Math.Clamp(Volume, 0, 1) * 100);
}
