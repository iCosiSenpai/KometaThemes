using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Snapshot of the full check, for the page.
/// </summary>
/// <param name="Running">Whether a check is in progress.</param>
/// <param name="Phase">Current step: <c>idle</c>, <c>matching</c> or <c>downloading</c>.</param>
/// <param name="Trigger">What started it: <c>schedule</c> or <c>manual</c>.</param>
/// <param name="Total">Anime to check.</param>
/// <param name="Done">Anime checked so far.</param>
/// <param name="Downloaded">Files downloaded so far.</param>
/// <param name="Failed">Anime with a problem so far.</param>
/// <param name="Current">Anime being checked.</param>
/// <param name="StartedUtc">When it started.</param>
/// <param name="FinishedUtc">When it finished.</param>
/// <param name="Message">Result or error.</param>
public sealed record SyncStatus(
    [property: JsonPropertyName("running")] bool Running,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("trigger")] string? Trigger,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("done")] int Done,
    [property: JsonPropertyName("downloaded")] int Downloaded,
    [property: JsonPropertyName("failed")] int Failed,
    [property: JsonPropertyName("current")] string? Current,
    [property: JsonPropertyName("startedUtc")] DateTime? StartedUtc,
    [property: JsonPropertyName("finishedUtc")] DateTime? FinishedUtc,
    [property: JsonPropertyName("message")] string? Message)
{
    /// <summary>Gets the idle state.</summary>
    public static SyncStatus Idle { get; } = new(false, "idle", null, 0, 0, 0, 0, null, null, null, null);
}
