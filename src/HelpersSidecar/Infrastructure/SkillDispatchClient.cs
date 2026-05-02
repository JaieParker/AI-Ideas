using Microsoft.Extensions.Options;

namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Production <see cref="ISkillDispatchClient"/>. Routes calls
/// to <c>http://127.0.0.1:5050/skills/{name}/dispatch</c> via
/// HTTP loopback into the same sidecar process. Translates
/// connection / timeout failures into a 599 result with the
/// exception message in the body so callers can render the
/// failure inline rather than throw.
/// </summary>
public sealed class SkillDispatchClient : ISkillDispatchClient
{
    private readonly HttpClient _http;
    private readonly SkillDispatchOptions _options;

    public SkillDispatchClient(HttpClient http, IOptions<SkillDispatchOptions> options)
    {
        _options = options.Value;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        if (_http.BaseAddress is null && !string.IsNullOrEmpty(_options.BaseUrl))
            _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<SkillDispatchResult> DispatchAsync(string skillName, IReadOnlyDictionary<string, string> form, CancellationToken ct = default)
    {
        var path = $"/skills/{Uri.EscapeDataString(skillName)}/dispatch";
        try
        {
            using var content = new FormUrlEncodedContent(
                form.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)));
            using var resp = await _http.PostAsync(path, content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new SkillDispatchResult((int)resp.StatusCode, body);
        }
        catch (TaskCanceledException ex)
        {
            return new SkillDispatchResult(599, $"timeout calling /skills/{skillName}/dispatch: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return new SkillDispatchResult(599, $"loopback call to /skills/{skillName}/dispatch failed: {ex.Message}");
        }
    }
}
