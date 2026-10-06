using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.KometaThemes.Http;

/// <summary>
/// Request budget for one remote API, shared by every request to it.
/// </summary>
/// <remarks>
/// <c>IHttpClientFactory</c> rebuilds handler chains every couple of minutes, so a limiter owned by a
/// handler was reset each time and allowed a fresh burst. The budget therefore lives in a singleton
/// and the handlers only borrow it.
/// </remarks>
public sealed class ApiThrottle : IDisposable
{
    /// <summary>Lowest accepted budget.</summary>
    public const int MinRatePerMinute = 1;

    /// <summary>Highest accepted budget: animethemes.moe allows 90 requests per minute.</summary>
    public const int MaxRatePerMinute = 90;

    private readonly Func<int> _ratePerMinute;
    private readonly object _gate = new();
    private TokenBucketRateLimiter _limiter;
    private int _currentRate;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiThrottle"/> class.
    /// </summary>
    /// <param name="name">API name, for logs.</param>
    /// <param name="ratePerMinute">Reads the current budget; re-read on every request so changes apply at once.</param>
    public ApiThrottle(string name, Func<int> ratePerMinute)
    {
        Name = name;
        _ratePerMinute = ratePerMinute;
        _currentRate = CurrentRate();
        _limiter = Build(_currentRate);
    }

    /// <summary>Gets the API name.</summary>
    public string Name { get; }

    /// <summary>
    /// Waits until the budget allows one more request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the request may go out.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var limiter = Current();
        while (true)
        {
            using var lease = await limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            if (lease.IsAcquired)
            {
                return;
            }

            // Queue full: wait for the next token instead of failing the request.
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            limiter = Current();
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
            _limiter.Dispose();
        }
    }

    private int CurrentRate() => Math.Clamp(_ratePerMinute(), MinRatePerMinute, MaxRatePerMinute);

    private TokenBucketRateLimiter Current()
    {
        var rate = CurrentRate();
        if (rate == Volatile.Read(ref _currentRate))
        {
            return _limiter;
        }

        lock (_gate)
        {
            if (rate != _currentRate)
            {
                // The old limiter is left to the GC instead of disposed: waiting requests hold leases on it.
                _limiter = Build(rate);
                Volatile.Write(ref _currentRate, rate);
            }

            return _limiter;
        }
    }

    private static TokenBucketRateLimiter Build(int ratePerMinute) => new(new TokenBucketRateLimiterOptions
    {
        // A small burst, then a steady pace: a full minute of tokens up front let a cold sync fire its
        // whole budget at once.
        TokenLimit = Math.Max(1, Math.Min(10, ratePerMinute)),
        ReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / ratePerMinute),
        TokensPerPeriod = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 500,
        AutoReplenishment = true
    });
}
