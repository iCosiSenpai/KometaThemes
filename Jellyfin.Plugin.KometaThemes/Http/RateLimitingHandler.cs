using System;
using System.Net.Http;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Http;

/// <summary>
/// HTTP handler that enforces a token bucket rate limit to avoid overwhelming the AnimeThemes API.
/// The bucket is re-evaluated on every request so changes to <see cref="Configuration.PluginConfiguration.RateLimitPerMinute"/>
/// from the dashboard take effect immediately.
/// </summary>
public sealed class RateLimitingHandler : DelegatingHandler
{
    private readonly ILogger<RateLimitingHandler> _logger;
    private readonly object _limiterLock = new();
    private TokenBucketRateLimiter _limiter;
    private int _currentRatePerMinute;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public RateLimitingHandler(ILogger<RateLimitingHandler> logger)
    {
        _logger = logger;
        _currentRatePerMinute = Math.Clamp(Plugin.Instance?.Configuration?.RateLimitPerMinute ?? 60, 1, 90);
        _limiter = BuildLimiter(_currentRatePerMinute);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        EnsureLimiterMatchesConfig();
        using var lease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);

        if (!lease.IsAcquired)
        {
            _logger.LogWarning("Rate limit exceeded, waiting for token availability");
            // Wait and retry once
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            using var retryLease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
            if (!retryLease.IsAcquired)
            {
                _logger.LogError("Rate limit still exceeded after retry");
                throw new InvalidOperationException("Rate limit exceeded");
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureLimiterMatchesConfig()
    {
        var newRate = Math.Clamp(Plugin.Instance?.Configuration?.RateLimitPerMinute ?? 60, 1, 90);
        if (newRate == _currentRatePerMinute)
        {
            return;
        }

        lock (_limiterLock)
        {
            if (newRate == _currentRatePerMinute)
            {
                return;
            }

            var oldLimiter = _limiter;
            _limiter = BuildLimiter(newRate);
            _currentRatePerMinute = newRate;
            _logger.LogInformation("Rate limit reconfigured to {Rate} req/min", newRate);
            try
            {
                oldLimiter.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispose previous rate limiter");
            }
        }
    }

    private static TokenBucketRateLimiter BuildLimiter(int ratePerMinute)
    {
        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = ratePerMinute,
            ReplenishmentPeriod = TimeSpan.FromSeconds(60.0 / ratePerMinute),
            TokensPerPeriod = 1,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 100,
            AutoReplenishment = true
        });
    }

    /// <summary>
    /// Disposes the rate limiter.
    /// </summary>
    /// <param name="disposing">Whether we're disposing.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _limiter.Dispose();
        }

        base.Dispose(disposing);
    }
}
