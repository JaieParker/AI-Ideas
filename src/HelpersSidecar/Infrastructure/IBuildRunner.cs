namespace HelpersSidecar.Infrastructure;

/// <summary>
/// Anti-corruption layer over external build commands (today:
/// <c>dotnet build</c>; future: any tier-managed component's
/// build pipeline). Per BR-PROCESS-011's stage step, the build
/// is the slow part — separating it behind an interface lets
/// tests run thousands of state-machine transitions without
/// shelling out, and lets future Plan-7 expansions (Go collector,
/// kai-platform components) plug in their own build runners
/// without touching <see cref="ProcessLifecycle"/>.
///
/// Per "Working Effectively with Legacy Code" (Feathers, 2004) §
/// "Sensing Variables" — Process.Start is exactly the kind of
/// seam this pattern was named for.
/// </summary>
public interface IBuildRunner
{
    /// <summary>
    /// Run <paramref name="command"/> with <paramref name="args"/>
    /// in <paramref name="workingDirectory"/>. Returns the exit code
    /// + captured stdout/stderr.
    /// </summary>
    Task<BuildResult> RunAsync(
        string workingDirectory,
        string command,
        IReadOnlyList<string> args,
        CancellationToken ct = default);
}

public sealed record BuildResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Succeeded => ExitCode == 0;
}
