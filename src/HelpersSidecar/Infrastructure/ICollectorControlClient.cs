namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Thin client over the Go collector's enrichmentctlextension HTTP
/// API on 127.0.0.1:13133. Production uses
/// <see cref="CollectorControlClient"/>; tests substitute a fake.
///
/// Returns nullable strings: <c>null</c> means "collector unreachable
/// (ECONNREFUSED / timeout)" — the caller renders a graceful-down
/// message rather than an error.
/// </summary>
public interface ICollectorControlClient
{
    Task<CollectorResponse?> GetSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default);
    Task<CollectorResponse?> SetSessionEnrichmentAsync(string sessionId, string key, string value, CancellationToken ct = default);
    Task<CollectorResponse?> RemoveSessionEnrichmentAsync(string sessionId, string key, CancellationToken ct = default);
    Task<CollectorResponse?> ClearSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default);

    Task<CollectorResponse?> GetSessionCollectionAsync(string sessionId, CancellationToken ct = default);
    Task<CollectorResponse?> SetSessionCollectionAsync(string sessionId, bool enabled, CancellationToken ct = default);

    Task<CollectorResponse?> GetPersistentAsync(string? key, CancellationToken ct = default);
    Task<CollectorResponse?> GetPersistentManyAsync(IEnumerable<string> keys, CancellationToken ct = default);
    Task<CollectorResponse?> SetPersistentAsync(string key, string value, CancellationToken ct = default);
    Task<CollectorResponse?> RemovePersistentAsync(string key, CancellationToken ct = default);
    Task<CollectorResponse?> ClearPersistentAsync(CancellationToken ct = default);

    Task<CollectorResponse?> RestartAsync(CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
}

public sealed record CollectorResponse(int StatusCode, string Body);
