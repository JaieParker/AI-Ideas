using System.Text.Json;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Application;

/// <summary>
/// CLI mode for the sidecar binary. Activated when the binary is
/// invoked with `--lifecycle &lt;verb&gt; &lt;component&gt;`. This mode
/// does NOT start Kestrel — it constructs a minimal
/// <see cref="ProcessLifecycle"/> directly, runs the requested
/// verb, prints JSON to stdout, and exits.
///
/// Used by /skill-bootstrap before the sidecar is up — the
/// /skill-bootstrap skill can't use the sidecar's HTTP endpoints
/// to manage the sidecar itself (chicken-and-egg), so it shells
/// out to the binary's CLI mode.
///
/// Verbs:
///   probe &lt;component&gt;     — print JSON status, exit 0.
///   sweep &lt;component&gt;     — kill zombies (if any), print JSON
///                              { swept: N }, exit 0.
///   stage &lt;component&gt;     — build + spawn green (BR-PROCESS-011).
///   promote &lt;component&gt;   — atomic swap blue ↔ green (BR-PROCESS-012).
///   discard &lt;component&gt;   — kill green; leave blue.
/// </summary>
public static class LifecycleCli
{
    public const string Flag = "--lifecycle";
    public const int SidecarPortDefault = 5050;
    public const int SidecarStagingPortDefault = 5051;
    public const string RuntimeDir = ".claude/runtime";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --lifecycle <probe|sweep|stage|promote|discard> <component>");
            return 2;
        }

        var verb = args[0];
        var component = args[1];

        var registry = ComponentRegistry.Default(
            sidecarPort: SidecarPortDefault,
            sidecarExe: ResolveSidecarExePath(),
            runtimeDir: RuntimeDir,
            sidecarStagingPort: SidecarStagingPortDefault);

        var lifecycle = new ProcessLifecycle(new PortProbe(), registry);

        switch (verb)
        {
            case "probe":
                {
                    var status = lifecycle.Probe(component);
                    PrintJson(status);
                    return 0;
                }
            case "sweep":
                {
                    var killed = await lifecycle.SweepZombiesAsync(component, ct);
                    PrintJson(new { component, swept = killed });
                    return 0;
                }
            case "stage":
                {
                    var result = await lifecycle.StageAsync(component, ct);
                    PrintJson(new { component, result.Outcome, result.GreenPid, result.Reason });
                    return result.Outcome == StageOutcome.Staged ? 0 : 1;
                }
            case "promote":
                {
                    var result = await lifecycle.PromoteAsync(component, ct);
                    PrintJson(new { component, result.Outcome, result.BluePid, result.Reason });
                    return result.Outcome == PromoteOutcome.Promoted ? 0 : 1;
                }
            case "discard":
                {
                    var result = await lifecycle.DiscardAsync(component, ct);
                    PrintJson(new { component, result.Outcome, result.Reason });
                    return result.Outcome == DiscardOutcome.Discarded ? 0 : 1;
                }
            default:
                Console.Error.WriteLine($"unknown verb '{verb}'");
                return 2;
        }
    }

    private static string ResolveSidecarExePath()
    {
        // The sidecar is invoked as `dotnet ...HelpersSidecar.dll`. The
        // CLI doesn't spawn the sidecar itself — that's /skill-bootstrap's
        // job via Bash run_in_background — so the exe path is informational.
        return Path.Combine("src", "HelpersSidecar", "bin", "Debug", "net10.0", "HelpersSidecar.dll");
    }

    private static void PrintJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = false,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        });
        Console.WriteLine(json);
    }
}
