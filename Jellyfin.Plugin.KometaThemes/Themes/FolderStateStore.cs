using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// Reads and writes the per-folder state file. Callers hold the folder's lock for writes.
/// </summary>
public sealed class FolderStateStore
{
    /// <summary>Name of the state file, the same as the 1.x tracker so upgrades keep their history.</summary>
    public const string FileName = "_kometa_themes.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly ILogger<FolderStateStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FolderStateStore"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public FolderStateStore(ILogger<FolderStateStore> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the state file path of a folder.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <returns>The path.</returns>
    public static string PathOf(string folder) => Path.Combine(folder, FileName);

    /// <summary>
    /// Loads a folder's state; a missing file gives an empty state.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The state.</returns>
    public async Task<FolderState> LoadAsync(string folder, CancellationToken cancellationToken)
    {
        var path = PathOf(folder);
        if (!File.Exists(path))
        {
            return new FolderState();
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read {Path}", path);
            return new FolderState();
        }

        try
        {
            return Parse(json);
        }
        catch (JsonException ex)
        {
            // Keep the damaged file aside instead of letting the next write replace it: it is the only
            // record of which files here KometaThemes owns.
            _logger.LogError(ex, "{Path} is damaged; moved it aside to {Path}.corrupt", path, path);
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (IOException moveEx)
            {
                _logger.LogWarning(moveEx, "Could not move {Path} aside", path);
            }

            return new FolderState();
        }
    }

    /// <summary>
    /// Writes a folder's state atomically, or removes the file when there is nothing left to remember.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SaveAsync(string folder, FolderState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = PathOf(folder);
        if (state.Records.Count == 0 && state.Overrides.Count == 0 && state.AnimeIds.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        state.Version = FolderState.CurrentVersion;
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, WriteOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Parses a state file in either format.
    /// </summary>
    /// <param name="json">File content.</param>
    /// <returns>The state.</returns>
    internal static FolderState Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            // 1.x: a bare list of records.
            return new FolderState
            {
                Version = 1,
                Records = document.RootElement.Deserialize<List<ThemeRecord>>() ?? []
            };
        }

        return document.RootElement.Deserialize<FolderState>() ?? new FolderState();
    }
}
