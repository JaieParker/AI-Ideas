namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Probes a component's <c>/healthz</c> endpoint via HTTP. Used
/// by <see cref="ProcessLifecycle"/>'s stage/promote state
/// machine (BR-PROCESS-011 / 012) to verify a spawned green is
/// healthy before promoting, and to verify blue is healthy
/// after the atomic swap.
///
/// Distinct from <see cref="IPortProbe"/> — port-probe answers
/// "is something listening" (cheap, OS-table); health-check
/// answers "is /healthz returning 200" (HTTP, depends on the
/// component being functional, not just bound).
/// </summary>
public interface IHealthChecker
{
    /// <summary>
    /// Poll <c>http://127.0.0.1:{port}/healthz</c> every
    /// <paramref name="interval"/> until 200 is returned or
    /// <paramref name="timeout"/> elapses. Returns true on
    /// healthy; false on timeout.
    /// </summary>
    Task<bool> WaitUntilHealthyAsync(int port, TimeSpan timeout, TimeSpan interval, CancellationToken ct = default);
}

public sealed class HttpHealthChecker : IHealthChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<bool> WaitUntilHealthyAsync(int port, TimeSpan timeout, TimeSpan interval, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var url = $"http://127.0.0.1:{port}/healthz";
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await Http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch (HttpRequestException) { /* not yet listening */ }
            catch (TaskCanceledException) { /* per-request timeout */ }
            try { await Task.Delay(interval, ct); }
            catch (TaskCanceledException) { return false; }
        }
        return false;
    }
}
