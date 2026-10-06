using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Why an item needs attention.
/// </summary>
public enum ProblemKind
{
    /// <summary>No anime entry was found.</summary>
    Unresolved,

    /// <summary>The entry was found but files could not be downloaded.</summary>
    DownloadFailed
}

/// <summary>
/// An item that needs attention, with when to try it again.
/// </summary>
public sealed class ProblemEntry
{
    /// <summary>Gets or sets the item ID.</summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the item name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the item year.</summary>
    [JsonPropertyName("productionYear")]
    public int? ProductionYear { get; set; }

    /// <summary>Gets or sets the kind of problem.</summary>
    [JsonPropertyName("reason")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProblemKind Reason { get; set; }

    /// <summary>Gets or sets the last error.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Gets or sets when the item was last tried.</summary>
    [JsonPropertyName("lastAttemptUtc")]
    public DateTime LastAttemptUtc { get; set; }

    /// <summary>Gets or sets how many times in a row it failed.</summary>
    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }

    /// <summary>Gets or sets when the next automatic attempt is due.</summary>
    [JsonPropertyName("nextAttemptUtc")]
    public DateTime NextAttemptUtc { get; set; }

    /// <summary>Gets or sets a hash of the item's names and IDs when it failed; a change retries at once.</summary>
    [JsonPropertyName("fingerprint")]
    public string? Fingerprint { get; set; }
}

/// <summary>
/// Remembers items that need attention and spaces out automatic retries.
/// </summary>
/// <remarks>
/// 1.x retried unresolved items on every check: on the owner's server two anime had been looked up
/// 209 times each. Unresolved items now wait 1, 3, 7 and then 30 days between automatic attempts,
/// failed downloads 1, 6 and 24 hours; a change to the item's titles or IDs retries at once, and the
/// owner can always retry by hand.
/// </remarks>
public sealed class FailedItemsStore : IDisposable
{
    private static readonly TimeSpan[] UnresolvedBackoff = [TimeSpan.FromDays(1), TimeSpan.FromDays(3), TimeSpan.FromDays(7), TimeSpan.FromDays(30)];
    private static readonly TimeSpan[] DownloadBackoff = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24)];

    private readonly ConcurrentDictionary<Guid, ProblemEntry> _entries = new();
    private readonly JsonFileStore<List<ProblemEntry>> _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="FailedItemsStore"/> class.
    /// </summary>
    /// <param name="paths">Application paths.</param>
    /// <param name="logger">Logger.</param>
    public FailedItemsStore(IApplicationPaths paths, ILogger<FailedItemsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _file = new JsonFileStore<List<ProblemEntry>>(Path.Combine(paths.PluginConfigurationsPath, "KometaThemes", "failed-items.json"), logger);
        foreach (var entry in _file.Load())
        {
            if (Guid.TryParse(entry.ItemId, out var id))
            {
                // 1.x entries have no schedule: retry them once, soon.
                if (entry.NextAttemptUtc == default)
                {
                    entry.Attempts = 0;
                    entry.NextAttemptUtc = DateTime.UtcNow;
                }

                _entries[id] = entry;
            }
        }
    }

    /// <summary>
    /// Records a failure.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="kind">Kind of problem.</param>
    /// <param name="error">Explanation.</param>
    public void Record(BaseItem item, ProblemKind kind, string? error)
    {
        ArgumentNullException.ThrowIfNull(item);
        var now = DateTime.UtcNow;
        var fingerprint = Fingerprint(item);
        _entries.AddOrUpdate(
            item.Id,
            _ => Create(item, kind, error, 1, now, fingerprint),
            (_, existing) => Create(item, kind, error, existing.Reason == kind ? existing.Attempts + 1 : 1, now, fingerprint));
        Persist();
    }

    /// <summary>
    /// Forgets an item's problem after a success, an exclusion or a manual match.
    /// </summary>
    /// <param name="itemId">Item ID.</param>
    public void Remove(Guid itemId)
    {
        if (_entries.TryRemove(itemId, out _))
        {
            Persist();
        }
    }

    /// <summary>
    /// Gets an item's problem.
    /// </summary>
    /// <param name="itemId">Item ID.</param>
    /// <returns>The entry, or null.</returns>
    public ProblemEntry? Get(Guid itemId) => _entries.TryGetValue(itemId, out var entry) ? entry : null;

    /// <summary>
    /// Tells whether an automatic check should try an item now.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="now">Current time.</param>
    /// <returns>True when the item has no problem, its wait is over, or it changed since.</returns>
    public bool IsDue(BaseItem item, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(item);
        return !_entries.TryGetValue(item.Id, out var entry)
            || now >= entry.NextAttemptUtc
            || !string.Equals(entry.Fingerprint, Fingerprint(item), StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets every entry.
    /// </summary>
    /// <returns>Entries keyed by item ID.</returns>
    public IReadOnlyDictionary<Guid, ProblemEntry> All() => new Dictionary<Guid, ProblemEntry>(_entries);

    /// <summary>
    /// Makes every item due again, for "retry everything".
    /// </summary>
    public void ResetSchedules()
    {
        foreach (var entry in _entries.Values)
        {
            entry.NextAttemptUtc = DateTime.UtcNow;
        }

        Persist();
    }

    /// <summary>
    /// Computes the wait before the next attempt.
    /// </summary>
    /// <param name="kind">Kind of problem.</param>
    /// <param name="attempts">Failures in a row.</param>
    /// <returns>The wait.</returns>
    internal static TimeSpan Backoff(ProblemKind kind, int attempts)
    {
        var steps = kind == ProblemKind.Unresolved ? UnresolvedBackoff : DownloadBackoff;
        return steps[Math.Clamp(attempts - 1, 0, steps.Length - 1)];
    }

    /// <summary>
    /// Hashes what identifies an item to the resolver: its names, year and provider IDs.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>A short hash.</returns>
    internal static string Fingerprint(BaseItem item)
    {
        var ids = string.Join(';', (item.ProviderIds ?? new Dictionary<string, string>()).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        var text = string.Join('|', item.Name, item.OriginalTitle, item.ProductionYear, ids);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8);
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    private static ProblemEntry Create(BaseItem item, ProblemKind kind, string? error, int attempts, DateTime now, string fingerprint) => new()
    {
        ItemId = item.Id.ToString("D"),
        Name = item.Name ?? string.Empty,
        ProductionYear = item.ProductionYear,
        Reason = kind,
        Error = error,
        LastAttemptUtc = now,
        Attempts = attempts,
        NextAttemptUtc = now + Backoff(kind, attempts),
        Fingerprint = fingerprint,
    };

    private void Persist() => _file.Save(() => _entries.Values.ToList());
}
