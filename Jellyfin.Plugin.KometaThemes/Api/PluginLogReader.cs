using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.KometaThemes.Api;

/// <summary>
/// Reads the Jellyfin server log and extracts the entries logged by this plugin.
/// Pure parsing logic, kept controller-free so it is unit-testable.
/// </summary>
public static partial class PluginLogReader
{
    /// <summary>
    /// Logger category prefix that identifies entries written by this plugin.
    /// </summary>
    public const string CategoryPrefix = "Jellyfin.Plugin.KometaThemes";

    /// <summary>
    /// Maximum number of entries the reader will ever return.
    /// </summary>
    public const int MaxEntries = 1000;

    private const int MaxContinuationChars = 4000;

    /// <summary>
    /// Finds the most recently written Jellyfin server log file. The "log_*.log"
    /// glob skips the FFmpeg transcode logs that share the same directory.
    /// </summary>
    /// <param name="logDirectoryPath">The Jellyfin log directory.</param>
    /// <returns>Full path of the newest log file, or null when none exists.</returns>
    public static string? FindNewestLogFile(string logDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(logDirectoryPath) || !Directory.Exists(logDirectoryPath))
        {
            return null;
        }

        return Directory.EnumerateFiles(logDirectoryPath, "log_*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Reads plugin entries from a log file on disk. The file is opened with
    /// permissive sharing because Serilog keeps it locked for writing.
    /// </summary>
    /// <param name="filePath">Path to the Jellyfin server log file.</param>
    /// <param name="maxEntries">Maximum number of (newest) entries to return.</param>
    /// <returns>Plugin log entries, oldest first.</returns>
    public static IReadOnlyList<PluginLogEntry> ReadPluginEntries(string filePath, int maxEntries)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParsePluginEntries(ReadLines(reader), maxEntries);
    }

    /// <summary>
    /// Parses plugin entries out of raw log lines. A line matching the Serilog
    /// header template starts a new entry; non-matching lines (exception stack
    /// traces) are appended to the in-progress entry when it belongs to the plugin.
    /// </summary>
    /// <param name="lines">Raw log lines in file order.</param>
    /// <param name="maxEntries">Maximum number of (newest) entries to return.</param>
    /// <returns>Plugin log entries, oldest first.</returns>
    public static IReadOnlyList<PluginLogEntry> ParsePluginEntries(IEnumerable<string> lines, int maxEntries)
    {
        var cap = Math.Clamp(maxEntries, 1, MaxEntries);
        var entries = new Queue<PluginLogEntry>(cap);
        PluginLogEntry? current = null;
        var currentIsPlugin = false;
        var headerRegex = HeaderRegex();

        foreach (var line in lines)
        {
            var match = headerRegex.Match(line);
            if (match.Success)
            {
                if (currentIsPlugin && current != null)
                {
                    Enqueue(entries, current, cap);
                }

                var category = match.Groups["cat"].Value;
                currentIsPlugin = category.StartsWith(CategoryPrefix, StringComparison.Ordinal);
                current = currentIsPlugin
                    ? CreateEntry(match)
                    : null;
            }
            else if (currentIsPlugin && current != null && current.Message.Length < MaxContinuationChars)
            {
                // Exception/stack-trace continuation of a plugin entry.
                current.Message += "\n" + line;
            }
        }

        if (currentIsPlugin && current != null)
        {
            Enqueue(entries, current, cap);
        }

        return entries.ToArray();
    }

    // Jellyfin default Serilog template:
    // [{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{ThreadId}] {SourceContext}: {Message}
    // The thread-id group is optional to tolerate customized logging.json templates.
    [GeneratedRegex(@"^\[(?<ts>[^\]]+)\] \[(?<lvl>[A-Z]{3})\](?: \[(?<thr>\d+)\])? (?<cat>[A-Za-z0-9_.<>+`]+): (?<msg>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    private static PluginLogEntry CreateEntry(Match match)
    {
        var rawTimestamp = match.Groups["ts"].Value;
        DateTimeOffset? timestamp = null;
        if (DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            timestamp = parsed;
        }

        return new PluginLogEntry
        {
            Timestamp = timestamp,
            RawTimestamp = rawTimestamp,
            Level = match.Groups["lvl"].Value,
            Category = match.Groups["cat"].Value,
            Message = match.Groups["msg"].Value
        };
    }

    private static void Enqueue(Queue<PluginLogEntry> entries, PluginLogEntry entry, int cap)
    {
        entries.Enqueue(entry);
        while (entries.Count > cap)
        {
            entries.Dequeue();
        }
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            yield return line;
        }
    }
}
