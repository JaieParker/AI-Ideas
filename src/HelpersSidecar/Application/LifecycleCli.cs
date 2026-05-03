using System.Text.Json;
using HelpersSidecar.Infrastructure;
using Microsoft.Extensions.Configuration;

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
            Console.Error.WriteLine("usage: --lifecycle <probe|sweep|stage|promote|discard|container-up|container-down|mode-direct|mode-container> <component>");
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
            case "container-up":
                return await ContainerUp(component, ct);
            case "container-down":
                return await ContainerDown(component, ct);
            case "mode-direct":
                return ModeSwitch(component, SidecarMode.Direct);
            case "mode-container":
                return ModeSwitch(component, SidecarMode.Container);
            default:
                Console.Error.WriteLine($"unknown verb '{verb}'");
                return 2;
        }
    }

    // ---------------- container verbs (BR-HELPERS-002 amended) ----------------

    private static async Task<int> ContainerUp(string component, CancellationToken ct)
    {
        if (component != "sidecar")
        {
            Console.Error.WriteLine($"container-up only valid for component 'sidecar' (got '{component}')");
            return 2;
        }

        var sidecar = LoadSidecarOptions();
        if (sidecar.Mode != SidecarMode.Container)
        {
            Console.Error.WriteLine(
                "container-up refused: Sidecar:Mode is 'Direct'. " +
                "Run --lifecycle mode-container sidecar first to switch (BR-SKILL-015 — skill rewrites allowed-tools in lockstep).");
            return 1;
        }

        var docker = new DockerCli();
        var version = await docker.VersionAsync(ct);
        if (!version.Succeeded)
        {
            Console.Error.WriteLine(
                "container-up refused: docker not invokable. " +
                "Install Docker Desktop (https://www.docker.com/products/docker-desktop) and retry.");
            return 1;
        }

        var name = $"claude-helpers-sidecar-{sidecar.Container.NameSuffix}";
        var result = await docker.RunDetachedAsync(
            sidecar.Container.Image,
            sidecar.Container.HostPort,
            name,
            sidecar.Container.PersistentEnrichmentsHostPath,
            ct);
        PrintJson(new
        {
            component,
            verb = "container-up",
            outcome = result.Succeeded ? "started" : "failed",
            container = name,
            hostPort = sidecar.Container.HostPort,
            image = sidecar.Container.Image,
            result.Stdout,
            result.Stderr,
        });
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> ContainerDown(string component, CancellationToken ct)
    {
        if (component != "sidecar")
        {
            Console.Error.WriteLine($"container-down only valid for component 'sidecar' (got '{component}')");
            return 2;
        }

        var sidecar = LoadSidecarOptions();
        var name = $"claude-helpers-sidecar-{sidecar.Container.NameSuffix}";
        var docker = new DockerCli();
        var result = await docker.StopAndRemoveAsync(name, ct);
        PrintJson(new
        {
            component,
            verb = "container-down",
            outcome = result.Succeeded ? "stopped" : "failed",
            container = name,
            result.Stdout,
            result.Stderr,
        });
        return result.Succeeded ? 0 : 1;
    }

    // ---------------- mode-switch verbs (BR-SKILL-015) ----------------

    private static int ModeSwitch(string component, SidecarMode targetMode)
    {
        if (component != "sidecar")
        {
            Console.Error.WriteLine($"mode-switch only valid for component 'sidecar' (got '{component}')");
            return 2;
        }

        var sidecar = LoadSidecarOptions();
        var rewriter = new SkillRewriter();
        const string skillsRoot = ".claude/skills";

        var spec = new RewriteSpec.SetSidecarMode(
            TargetMode: targetMode,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: sidecar.Container.Image,
            HostPort: sidecar.Container.HostPort,
            ContainerNamePrefix: $"claude-helpers-sidecar-{sidecar.Container.NameSuffix}");

        var reports = rewriter.Repair(skillsRoot, spec);
        var drifted = reports.Count(r => r.Drifted);

        PrintJson(new
        {
            component,
            verb = targetMode == SidecarMode.Container ? "mode-container" : "mode-direct",
            targetMode = targetMode.ToString(),
            filesScanned = reports.Count,
            filesRewritten = drifted,
            note = "remember to set Sidecar:Mode in appsettings.Local.json so the runtime config matches",
        });
        return 0;
    }

    private static SidecarOptions LoadSidecarOptions()
    {
        // Mirror Program.cs's BR-CODE-002 loading order: appsettings.json,
        // appsettings.{Env}.json, appsettings.Local.json — all from the
        // binary's directory.
        var binDir = AppContext.BaseDirectory;
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(binDir, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(binDir, $"appsettings.{env}.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(binDir, "appsettings.Local.json"), optional: true, reloadOnChange: false)
            .Build();

        var opts = new SidecarOptions();
        config.GetSection(SidecarOptions.SectionName).Bind(opts);
        return opts;
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
