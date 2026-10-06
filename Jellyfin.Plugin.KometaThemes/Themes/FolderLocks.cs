using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.KometaThemes.Themes;

/// <summary>
/// One lock per media folder, so the full check, a single-item action and a library event never write
/// the same theme files or state file at the same time.
/// </summary>
/// <remarks>
/// In 1.x the per-item sync triggered by a new library item ran outside the full sync's guard, and both
/// could encode into the same temporary file.
/// </remarks>
public sealed class FolderLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>
    /// Waits for exclusive access to a folder.
    /// </summary>
    /// <param name="folder">Folder path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A handle that releases the folder when disposed.</returns>
    public async Task<IDisposable> AcquireAsync(string folder, CancellationToken cancellationToken)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
