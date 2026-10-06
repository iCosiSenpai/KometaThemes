using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.KometaThemes.Models;

// Working data handed between the planner and the installer, not a public contract.
#pragma warning disable CA1002

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// A file that should exist in a folder.
/// </summary>
/// <param name="Choice">The theme and the version to use.</param>
/// <param name="Media">Audio or video.</param>
/// <param name="FileName">Target file name.</param>
/// <param name="Volume">Volume to bake in, in percent.</param>
/// <param name="Kept">Whether the owner asked to keep it regardless of the settings.</param>
public sealed record DesiredFile(ThemeChoice Choice, MediaType Media, string FileName, int Volume, bool Kept);

/// <summary>
/// What has to change in a folder.
/// </summary>
public sealed class ThemePlan
{
    /// <summary>Gets files that are already right.</summary>
    public List<(ThemeRecord Record, DesiredFile Desired)> Keep { get; } = [];

    /// <summary>Gets files that are right but carry an old name.</summary>
    public List<(ThemeRecord Record, DesiredFile Desired)> Rename { get; } = [];

    /// <summary>Gets files to fetch; <c>Replaces</c> is set when an existing file must give way (volume changed).</summary>
    public List<(DesiredFile Desired, ThemeRecord? Replaces)> Download { get; } = [];

    /// <summary>Gets files KometaThemes wrote that are no longer wanted.</summary>
    public List<ThemeRecord> Delete { get; } = [];

    /// <summary>Gets records whose file is gone, to forget.</summary>
    public List<ThemeRecord> Forget { get; } = [];

    /// <summary>Gets a value indicating whether the folder already matches.</summary>
    public bool IsEmpty => Rename.Count == 0 && Download.Count == 0 && Delete.Count == 0 && Forget.Count == 0;
}

/// <summary>
/// Compares what a folder holds with what it should hold. Pure: no disk or network access.
/// </summary>
/// <remarks>
/// Only records with <see cref="ThemeSource.AnimeThemes"/> as source take part. Imported files and
/// files KometaThemes did not write are never renamed or removed: 1.x deleted any theme it could not
/// account for on a forced sync, including the owner's own files.
/// </remarks>
public static class ThemePlanner
{
    /// <summary>
    /// Builds the plan for one folder.
    /// </summary>
    /// <param name="records">Records of the folder's state file.</param>
    /// <param name="desired">Files that should exist.</param>
    /// <param name="fileExists">Tells whether a file exists, given its folder name and file name.</param>
    /// <returns>The plan.</returns>
    public static ThemePlan Build(IReadOnlyList<ThemeRecord> records, IReadOnlyList<DesiredFile> desired, Func<string, string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(fileExists);

        var plan = new ThemePlan();
        var claimed = new HashSet<ThemeRecord>();

        foreach (var record in records)
        {
            if (!fileExists(record.EffectiveDirectory, record.FileName))
            {
                plan.Forget.Add(record);
                claimed.Add(record);
            }
        }

        foreach (var want in desired)
        {
            var match = records.FirstOrDefault(record =>
                !claimed.Contains(record)
                && record.Source == ThemeSource.AnimeThemes
                && record.ThemeId == want.Choice.Theme.Id
                && record.Media == want.Media);

            if (match == null)
            {
                plan.Download.Add((want, null));
                continue;
            }

            claimed.Add(match);

            // A record without a known volume is taken as already right, so an upgrade never
            // re-downloads a library just because 1.x did not write the volume down.
            var volume = match.EffectiveVolume ?? want.Volume;
            if (volume != want.Volume)
            {
                plan.Download.Add((want, match));
            }
            else if (!string.Equals(match.FileName, want.FileName, StringComparison.Ordinal))
            {
                plan.Rename.Add((match, want));
            }
            else
            {
                plan.Keep.Add((match, want));
            }
        }

        foreach (var record in records)
        {
            if (!claimed.Contains(record) && record.Source == ThemeSource.AnimeThemes)
            {
                plan.Delete.Add(record);
            }
        }

        return plan;
    }

    /// <summary>
    /// Turns the chosen themes into the files a folder should hold, applying the owner's overrides and
    /// giving every file a unique name.
    /// </summary>
    /// <param name="audio">Themes chosen as songs by the settings.</param>
    /// <param name="video">Themes chosen as videos by the settings.</param>
    /// <param name="catalog">Every theme of the target, for overrides that keep a theme the settings skip.</param>
    /// <param name="state">The folder's state.</param>
    /// <param name="audioVolume">Song volume in percent.</param>
    /// <param name="videoVolume">Video volume in percent.</param>
    /// <param name="foreignNames">Names in the theme folders that KometaThemes does not manage, per folder.</param>
    /// <returns>The desired files.</returns>
    public static IReadOnlyList<DesiredFile> Desired(
        IReadOnlyList<ThemeChoice> audio,
        IReadOnlyList<ThemeChoice> video,
        IReadOnlyList<ThemeChoice> catalog,
        FolderState state,
        int audioVolume,
        int videoVolume,
        IReadOnlyDictionary<string, HashSet<string>> foreignNames)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new List<DesiredFile>();
        foreach (var media in new[] { MediaType.Audio, MediaType.Video })
        {
            var chosen = media == MediaType.Audio ? audio : video;
            var volume = media == MediaType.Audio ? audioVolume : videoVolume;
            var directory = media == MediaType.Audio ? ThemeFileKinds.AudioDirectory : ThemeFileKinds.VideoDirectory;
            var taken = new HashSet<string>(
                foreignNames.TryGetValue(directory, out var foreign) ? foreign : [],
                StringComparer.OrdinalIgnoreCase);

            var picks = chosen
                .Where(choice => state.OverrideFor(choice.Theme.Id, media) != false)
                .Select(choice => (Choice: choice, Kept: state.OverrideFor(choice.Theme.Id, media) == true))
                .ToList();

            foreach (var choice in catalog)
            {
                if (state.OverrideFor(choice.Theme.Id, media) == true && picks.All(p => p.Choice.Theme.Id != choice.Theme.Id))
                {
                    picks.Add((choice, true));
                }
            }

            foreach (var (choice, kept) in picks)
            {
                var extension = media == MediaType.Audio ? ".mp3" : VideoExtension(choice);
                var name = ThemeNaming.Unique(ThemeNaming.FileName(choice.Code, choice.Title, extension), taken);
                taken.Add(name);
                result.Add(new DesiredFile(choice, media, name, volume, kept));
            }
        }

        return result;
    }

    private static string VideoExtension(ThemeChoice choice)
    {
        var extension = Path.GetExtension(choice.Video.Basename ?? choice.Video.Link ?? string.Empty);
        return ThemeFileKinds.IsVideoFile("x" + extension) ? extension.ToLowerInvariant() : ".webm";
    }
}
