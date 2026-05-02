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
///   probe &lt;component&gt;  — print JSON status, exit 0.
///   sweep &lt;component&gt;  — kill zombies (if any), print JSON
///                          { swept: N }, exit 0.
/// </summary>
public static class LifecycleCli
{
    public const string Flag = "--lifecycle";
    public const int SidecarPortDefault = 5050;
    public const string RuntimeDir = ".claude/runtime";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: --lifecycle <probe|sweep> <component>");
            return 2;
        }

        var verb = args[0];
        var component = args[1];

        var registry = ComponentRegistry.Default(
            sidecarPort: SidecarPortDefault,
            sidecarExe: ResolveSidecarExePath(),
            runtimeDir: RuntimeDir);

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
