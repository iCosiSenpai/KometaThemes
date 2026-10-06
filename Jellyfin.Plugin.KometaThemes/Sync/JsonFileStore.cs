using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Sync;

/// <summary>
/// Small JSON file in the plugin's data folder, written atomically and at most every few seconds.
/// </summary>
/// <typeparam name="T">Stored type.</typeparam>
public sealed class JsonFileStore<T> : IDisposable
    where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private Func<T>? _pending;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileStore{T}"/> class.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="logger">Logger.</param>
    public JsonFileStore(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Reads the file; a missing or damaged file gives a new value (a damaged one is kept aside).
    /// </summary>
    /// <returns>The value.</returns>
    public T Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new T();
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path)) ?? new T();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "{Path} is damaged; moved it aside", _path);
            try
            {
                File.Move(_path, _path + ".corrupt", overwrite: true);
            }
            catch (IOException moveEx)
            {
                _logger.LogWarning(moveEx, "Could not move {Path} aside", _path);
            }

            return new T();
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read {Path}", _path);
            return new T();
        }
    }

    /// <summary>
    /// Schedules a write. The snapshot is taken when the write happens, so callers pass a function.
    /// </summary>
    /// <param name="snapshot">Produces the value to write.</param>
    public void Save(Func<T> snapshot)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = snapshot;
            _timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Writes a pending change now.
    /// </summary>
    public void Flush()
    {
        Func<T>? snapshot;
        lock (_gate)
        {
            snapshot = _pending;
            _pending = null;
        }

        if (snapshot == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot(), Options));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write {Path}", _path);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
        Flush();
    }
}
