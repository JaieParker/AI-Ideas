namespace HelpersSidecar.Domain;

/// <summary>
/// Per-phase commit-message prefix map. Indexed by
/// <see cref="ExtendPhase"/>. Each domain owns its own copy;
/// OTEL uses <c>"plan: " / "feat(otel): " / "chore: " / "test: "</c>.
/// Future domains may diverge.
/// </summary>
public sealed record CommitConventions(IReadOnlyDictionary<ExtendPhase, string> Prefixes)
{
    public string PrefixFor(ExtendPhase phase) =>
        Prefixes.TryGetValue(phase, out var p)
            ? p
            : throw new KeyNotFoundException($"no commit prefix configured for phase {phase}");
}

/// <summary>
/// The phases of the self-modification flow defined by
/// BR-EXTEND-002. Plan-6 will introduce a Phase1_5 for the
/// architecture-review gate (BR-PROCESS-009); kept out of v1
/// here so the value-set stays stable.
/// </summary>
public enum ExtendPhase
{
    Plan,
    Implement,
    Build,
    Test,
}
