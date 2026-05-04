using System.Collections.Concurrent;

namespace HelpersSidecar.Domain;

/// <summary>
/// In-memory store for active /demo runs. Plan-23 split /demo's
/// dispatch into "emit plan" (DemoDispatchEndpoint) and "record
/// results" (DemoObserveEndpoint); the run_id correlates the two
/// halves. Runs are evicted once finalised; runs that exceed
/// <see cref="StaleAfter"/> without finalisation are evicted by
/// the next register call (best-effort cleanup).
/// </summary>
public interface IDemoRunStore
{
    void Register(DemoRunInputs run);
    DemoRunInputs? Get(string runId);
    void RecordStep(string runId, DemoStepResult result);
    DemoRunInputs? Finalise(string runId);
}

/// <summary>Snapshot of a /demo run from dispatch through observe.</summary>
public sealed record DemoRunInputs(
    string RunId,
    string SessionId,
    string TargetName,
    string TargetKind,
    string DemoName,
    DateTimeOffset DemoStartedAt,
    IReadOnlyList<DemoStepDescriptor> Steps,
    IReadOnlyList<DemoPreflightRow> Preflight,
    int PreflightPass,
    int PreflightTotal,
    string TeardownText,
    bool NoReport)
{
    public ConcurrentDictionary<int, DemoStepResult> Results { get; } = new();
}

internal sealed class DemoRunStore : IDemoRunStore
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, DemoRunInputs> _runs = new();

    public void Register(DemoRunInputs run)
    {
        // Evict stale runs (best-effort).
        var threshold = DateTimeOffset.UtcNow - StaleAfter;
        foreach (var (id, r) in _runs)
        {
            if (r.DemoStartedAt < threshold)
                _runs.TryRemove(id, out _);
        }
        _runs[run.RunId] = run;
    }

    public DemoRunInputs? Get(string runId) =>
        _runs.TryGetValue(runId, out var r) ? r : null;

    public void RecordStep(string runId, DemoStepResult result)
    {
        if (_runs.TryGetValue(runId, out var r))
            r.Results[result.Number] = result;
    }

    public DemoRunInputs? Finalise(string runId)
    {
        _runs.TryRemove(runId, out var r);
        return r;
    }
}
