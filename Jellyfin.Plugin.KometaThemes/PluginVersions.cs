using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.KometaThemes;

/// <summary>
/// Retires older copies of this plugin that an update left next to the running one.
/// </summary>
/// <remarks>
/// Jellyfin groups the versions of a plugin by the name in each folder's <c>meta.json</c>. When a
/// catalog install and a loaded plugin write different names there, an update leaves the old copy
/// active too and both load (seen with AnimeClick). Marking older active copies Superseded makes
/// Jellyfin skip them from the next start. Nothing is deleted.
/// </remarks>
public static class PluginVersions
{
    /// <summary>
    /// Marks older active copies of a plugin as superseded.
    /// </summary>
    /// <param name="pluginsPath">Jellyfin's plugin folder.</param>
    /// <param name="id">Plugin ID.</param>
    /// <param name="current">Running version.</param>
    /// <param name="currentDirectory">Folder of the running copy.</param>
    /// <returns>Folders whose manifest was changed. Never throws.</returns>
    public static IReadOnlyList<string> SupersedeOlderCopies(string? pluginsPath, Guid id, Version current, string? currentDirectory)
    {
        var changed = new List<string>();
        if (string.IsNullOrWhiteSpace(pluginsPath) || !Directory.Exists(pluginsPath) || current == null)
        {
            return changed;
        }

        List<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(pluginsPath).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return changed;
        }

        var own = currentDirectory == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
        foreach (var folder in folders)
        {
            if (own != null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), own, StringComparison.Ordinal))
            {
                continue;
            }

            var metaFile = Path.Combine(folder, "meta.json");
            try
            {
                if (!File.Exists(metaFile) || JsonNode.Parse(File.ReadAllText(metaFile)) is not JsonObject manifest)
                {
                    continue;
                }

                if (!Guid.TryParse(Text(manifest, "guid") ?? Text(manifest, "id"), out var guid) || guid != id
                    || !Version.TryParse(Text(manifest, "version"), out var version) || version >= current
                    || Text(manifest, "status") is not (null or "Active"))
                {
                    continue;
                }

                manifest["status"] = "Superseded";
                var temporary = metaFile + ".kometathemes.tmp";
                File.WriteAllText(temporary, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, metaFile, overwrite: true);
                changed.Add(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A folder that cannot be read or written stays as it is.
            }
        }

        return changed;
    }

    private static string? Text(JsonObject manifest, string name)
    {
        var node = manifest.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
