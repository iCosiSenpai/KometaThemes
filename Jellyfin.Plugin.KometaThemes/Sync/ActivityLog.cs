using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Kind of activity entry.
/// </summary>
public enum ActivityKind
{
    /// <summary>Themes were downloaded.</summary>
    Downloaded,

    /// <summary>Themes were removed.</summary>
    Removed,

    /// <summary>Files were renamed to the 2.0 naming.</summary>
    Renamed,

    /// <summary>An item was matched to an anime entry by the owner.</summary>
    Matched,

    /// <summary>No anime entry was found.</summary>
    NotFound,

    /// <summary>Something failed.</summary>
    Failed,

    /// <summary>A theme was imported from YouTube.</summary>
    Imported,

    /// <summary>A full check finished.</summary>
    Checked
}

/// <summary>
/// One line of the activity list.
/// </summary>
/// <param name="TimeUtc">When it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="ItemId">The item, if any.</param>
/// <param name="ItemName">Its name.</param>
/// <param name="Message">What happened, in plain words.</param>
public sealed record ActivityEntry(
    [property: JsonPropertyName("timeUtc")] DateTime TimeUtc,
    [property: JsonPropertyName("kind")][property: JsonConverter(typeof(JsonStringEnumConverter))] ActivityKind Kind,
    [property: JsonPropertyName("itemId")] Guid? ItemId,
    [property: JsonPropertyName("itemName")] string? ItemName,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// The latest things KometaThemes did, in words the owner can read. Replaces the raw log viewer of 1.x;
/// Jellyfin's own log keeps the details.
/// </summary>
public sealed class ActivityLog : IDisposable
{
    private const int Capacity = 300;

    private readonly object _gate = new();
    private readonly LinkedList<ActivityEntry> _entries = new();
    private readonly JsonFileStore<List<ActivityEntry>> _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActivityLog"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger.</param>
    public ActivityLog(IApplicationPaths paths, ILogger<ActivityLog> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = new JsonFileStore<List<ActivityEntry>>(Path.Combine(paths.PluginConfigurationsPath, "KometaThemes", "activity.json"), logger);
        foreach (var entry in _file.Load().OrderByDescending(e => e.TimeUtc).Take(Capacity))
        {
            _entries.AddLast(entry);
        }
    }

    /// <summary>
    /// Adds an entry.
    /// </summary>
    /// <param name="kind">What happened.</param>
    /// <param name="itemId">The item, if any.</param>
    /// <param name="itemName">Its name.</param>
    /// <param name="message">What happened, in plain words.</param>
    public void Add(ActivityKind kind, Guid? itemId, string? itemName, string message)
    {
        lock (_gate)
        {
            _entries.AddFirst(new ActivityEntry(DateTime.UtcNow, kind, itemId, itemName, message));
            while (_entries.Count > Capacity)
            {
                _entries.RemoveLast();
            }
        }

        _file.Save(Snapshot);
    }

    /// <summary>
    /// Gets the newest entries.
    /// </summary>
    /// <param name="limit">Most entries to return.</param>
    /// <param name="itemId">Only entries of this item, if set.</param>
    /// <returns>Entries, newest first.</returns>
    public IReadOnlyList<ActivityEntry> Latest(int limit, Guid? itemId = null)
    {
        lock (_gate)
        {
            return _entries.Where(e => itemId == null || e.ItemId == itemId).Take(Math.Clamp(limit, 1, Capacity)).ToList();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    private List<ActivityEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }
}
