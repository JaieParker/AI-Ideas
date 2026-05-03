using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production <see cref="ICollectorControlClient"/>. All methods
/// translate "the collector isn't reachable" into a <c>null</c>
/// return so callers can degrade gracefully.
///
/// Per <c>BR-OTEL-007</c>, the control and healthz base URLs are
/// resolved from <see cref="CollectorOptions"/> — never hardcoded.
/// </summary>
public sealed class CollectorControlClient : ICollectorControlClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private readonly string _controlBase;
    private readonly string _healthBase;

    public CollectorControlClient(IOptions<CollectorOptions> options)
    {
        var o = options.Value;
        _controlBase = $"{o.CollectorScheme}://{o.CollectorHost}:{o.CollectorControlPort}";
        _healthBase  = $"{o.CollectorScheme}://{o.CollectorHost}:{o.CollectorHealthzPort}";
    }

    public Task<CollectorResponse?> GetSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/sessions/{Esc(sessionId)}/enrichments", null, ct);

    public Task<CollectorResponse?> SetSessionEnrichmentAsync(string sessionId, string key, string value, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"/sessions/{Esc(sessionId)}/enrichments",
            JsonContent(new { op = "set", key, value }), ct);

    public Task<CollectorResponse?> RemoveSessionEnrichmentAsync(string sessionId, string key, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"/sessions/{Esc(sessionId)}/enrichments",
            JsonContent(new { op = "remove", key }), ct);

    public Task<CollectorResponse?> ClearSessionEnrichmentsAsync(string sessionId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"/sessions/{Esc(sessionId)}/enrichments",
            JsonContent(new { op = "clear" }), ct);

    public Task<CollectorResponse?> GetSessionCollectionAsync(string sessionId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, $"/sessions/{Esc(sessionId)}/collection", null, ct);

    public Task<CollectorResponse?> SetSessionCollectionAsync(string sessionId, bool enabled, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"/sessions/{Esc(sessionId)}/collection",
            JsonContent(new { enabled }), ct);

    public Task<CollectorResponse?> GetPersistentAsync(string? key, CancellationToken ct = default)
        => key is null
            ? SendAsync(HttpMethod.Get, "/persistent-enrichments", null, ct)
            : SendAsync(HttpMethod.Get, $"/persistent-enrichments/{Esc(key)}", null, ct);

    public Task<CollectorResponse?> GetPersistentManyAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var qs = string.Join("&", keys.Select(k => $"keys={Esc(k)}"));
        return SendAsync(HttpMethod.Get, $"/persistent-enrichments?{qs}", null, ct);
    }

    public Task<CollectorResponse?> SetPersistentAsync(string key, string value, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "/persistent-enrichments",
            JsonContent(new { op = "set", key, value }), ct);

    public Task<CollectorResponse?> RemovePersistentAsync(string key, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "/persistent-enrichments",
            JsonContent(new { op = "remove", key }), ct);

    public Task<CollectorResponse?> ClearPersistentAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "/persistent-enrichments",
            JsonContent(new { op = "clear" }), ct);

    public Task<CollectorResponse?> RestartAsync(CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "/control/restart", null, ct);

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var r = await Http.GetAsync($"{_healthBase}/", ct);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<CollectorResponse?> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(method, _controlBase + path) { Content = content };
            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new CollectorResponse((int)resp.StatusCode, body);
        }
        catch (HttpRequestException ex) when (IsConnectRefused(ex))
        {
            return null;
        }
        catch (TaskCanceledException) { return null; }
        catch (SocketException) { return null; }
    }

    private static bool IsConnectRefused(HttpRequestException ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is SocketException) return true;
            inner = inner.InnerException;
        }
        return false;
    }

    private static StringContent JsonContent(object o)
        => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static string Esc(string s) => Uri.EscapeDataString(s);
}
