using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.KometaThemes.Http;

/// <summary>
/// Waits for the shared budget of an API before every attempt, retries included.
/// </summary>
public sealed class ThrottleHandler : DelegatingHandler
{
    private readonly ApiThrottle _throttle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottleHandler"/> class.
    /// </summary>
    /// <param name="throttle">The API budget.</param>
    public ThrottleHandler(ApiThrottle throttle)
    {
        _throttle = throttle;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
