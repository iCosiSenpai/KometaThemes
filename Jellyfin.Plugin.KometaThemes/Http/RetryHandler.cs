using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KometaThemes.Http;

/// <summary>
/// Retries transient failures with backoff and gives each attempt its own time limit.
/// </summary>
/// <remarks>
/// <para>
/// 1.x used Polly, which Jellyfin 12 only carries as an internal dependency of its database layer: a
/// plugin relying on it breaks the day the server drops it. Its timeout also escaped as a
/// <c>TimeoutRejectedException</c> that no caller handled, so one slow response aborted a whole sync.
/// Here a timed-out attempt is retried like any other transient failure, and when retries run out the
/// caller always gets an <see cref="HttpRequestException"/>.
/// </para>
/// <para>
/// The time limit covers the response headers. Streaming callers (theme downloads) read the body
/// under their own deadline.
/// </para>
/// </remarks>
public sealed class RetryHandler : DelegatingHandler
{
    private readonly ILogger<RetryHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public RetryHandler(ILogger<RetryHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets or sets the number of retries after the first attempt.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Gets or sets the time limit of one attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Gets or sets the first backoff delay; each retry doubles it.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the longest delay honoured from a Retry-After header.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Tells whether a status code is worth retrying.
    /// </summary>
    /// <param name="status">HTTP status.</param>
    /// <returns>True for 408, 429 and 5xx.</returns>
    public static bool IsTransient(HttpStatusCode status)
        => status == HttpStatusCode.RequestTimeout || status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            HttpRequestException? failure = null;
            using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attemptCts.CancelAfter(AttemptTimeout);
                try
                {
                    response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                    if (!IsTransient(response.StatusCode) || attempt >= MaxRetries)
                    {
                        return response;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = new HttpRequestException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{request.RequestUri?.Host} did not answer within {AttemptTimeout.TotalSeconds:0} seconds."));
                }
                catch (HttpRequestException ex)
                {
                    failure = ex;
                }
            }

            if (failure != null && attempt >= MaxRetries)
            {
                throw failure;
            }

            var delay = NextDelay(attempt, response);
            _logger.LogWarning(
                "Request to {Host} failed ({Reason}); retry {Attempt} of {Max} in {Delay} s",
                request.RequestUri?.Host,
                response != null ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) : failure?.Message,
                attempt + 1,
                MaxRetries,
                (int)delay.TotalSeconds);

            // The failed response is replaced by the next attempt; dispose it so its connection returns to the pool.
            response?.Dispose();
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private TimeSpan NextDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta < MaxDelay ? delta : MaxDelay;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait < MaxDelay ? wait : MaxDelay;
            }
        }

        var backoff = BaseDelay * Math.Pow(2, attempt);
        return backoff < MaxDelay ? backoff : MaxDelay;
    }
}
