namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Stage/promote/discard lifecycle for components opting in via
/// <see cref="StagingSpec"/> (BR-PROCESS-011). A component
/// without a <see cref="ComponentSpec"/>.<c>Staging</c> raises
/// <see cref="NotSupportedException"/> on these calls.
///
/// State machine (BR-PROCESS-012 — atomic promote with rollback):
///
/// <code>
/// Initial state: blue running on Port; no green.
///
/// Stage:   build to StagingPath → spawn green on StagingPort →
///          poll StagingPort/healthz → return StageResult.
///          Refuses if green already running (AlreadyStaged).
///
/// Promote: probe green health → snapshot blue binary → kill blue
///          → copy staged binary → restart blue → verify blue
///          health → kill green. On verify-fail at any point
///          after kill-blue: restore the snapshot, restart blue,
///          leave green alive for inspection. State machine
///          NEVER lands in "no blue, no green" except on
///          explicit user discard + stop.
///
/// Discard: kill green, delete green PID file. Blue unchanged.
/// </code>
/// </summary>
public interface IStageableLifecycle
{
    Task<StageResult>   StageAsync(string componentName, CancellationToken ct = default);
    Task<PromoteResult> PromoteAsync(string componentName, CancellationToken ct = default);
    Task<DiscardResult> DiscardAsync(string componentName, CancellationToken ct = default);
}

public enum StageOutcome
{
    Staged,           // build succeeded; green is running and healthy
    BuildFailed,      // build returned non-zero; no green
    AlreadyStaged,    // green is already running; discard before re-staging
    NotStageable,     // component has no StagingSpec
    HealthCheckFailed, // green spawned but didn't reach healthy within timeout
}

public sealed record StageResult(
    StageOutcome Outcome,
    int? GreenPid,
    string Reason,
    string? BuildStdout = null,
    string? BuildStderr = null);

public enum PromoteOutcome
{
    Promoted,             // blue is running on the new binary; green is gone
    RolledBack,           // verify failed; blue restored from snapshot; green still running
    NoGreenStaged,        // nothing to promote
    GreenNotHealthy,      // green wasn't healthy at promote time
    NotStageable,         // component has no StagingSpec
}

public sealed record PromoteResult(
    PromoteOutcome Outcome,
    int? BluePid,
    string Reason);

public enum DiscardOutcome
{
    Discarded,
    NoGreenStaged,
    NotStageable,
}

public sealed record DiscardResult(
    DiscardOutcome Outcome,
    string Reason);
