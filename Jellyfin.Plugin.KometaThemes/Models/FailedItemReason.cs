namespace Jellyfin.Plugin.KometaThemes.Models;

/// <summary>
/// Why a library item ended up in the failed items list.
/// </summary>
public enum FailedItemReason
{
    /// <summary>
    /// No provider (external IDs or title fallback) could match the item to an anime.
    /// </summary>
    Unresolved,

    /// <summary>
    /// The item was resolved but downloading its themes threw an error.
    /// </summary>
    DownloadFailed
}
