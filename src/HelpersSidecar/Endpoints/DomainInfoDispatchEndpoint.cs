using System.Text.Json;
using System.Text.Json.Serialization;
using HelpersSidecar.Artefacts;
using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Endpoints;

/// <summary>
/// Dispatch endpoint for the /domain-info skill (Plan-5 Phase 2e).
///
/// Returns any subset of a resolved <see cref="IDomain"/>'s
/// knowledge slices in a single response. Read-only — never
/// mutates anything.
///
/// Request form: <c>domain=&lt;name&gt;</c> and optionally
/// <c>slices=&lt;comma-separated&gt;</c>. Slices is one of:
///
///   name, plan-files, commits, governed-globs, playbook-path,
///   glossary, business-rules-path, trusted-references, all
///
/// Response is text/plain with a structured key-value layout
/// per slice (consumers either render as-is or post-parse).
/// </summary>
public static class DomainInfoDispatchEndpoint
{
    public static IEndpointRouteBuilder MapDomainInfoDispatch(this IEndpointRouteBuilder app)
    {
        app.MapPost("/skills/domain-info/dispatch", Handle)
            .WithName("DomainInfoDispatch")
            .WithSummary("Skill dispatcher for /domain-info — read-only domain-knowledge query")
            .WithDescription("Form-encoded session_id, args (<domain> [<slices>]). " +
                "Returns the requested slices of the resolved IDomain in one response. " +
                "BR-EXTEND-006 / BR-EXTEND-008.");
        return app;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task<IResult> Handle(HttpContext ctx, IDomainResolver domains, IArtefactRegistry artefacts)
    {
        var form = await ctx.Request.ReadFormAsync();
        var args = form["args"].ToString().Trim();

        if (string.IsNullOrEmpty(args))
            return Text($"usage: /domain-info <domain> [<slices>]\n" +
                        $"known domains: {string.Join(", ", domains.KnownNames)}\n" +
                        $"slices: name, plan-files, commits, governed-globs, playbook-path, glossary, business-rules-path, trusted-references, all (default)");

        var tokens = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var domainName = tokens[0];
        var slicesArg = tokens.Length > 1 ? tokens[1] : "all";

        if (!domains.TryResolve(domainName, out var domain))
            return Text($"domain-info failed: unknown domain '{domainName}' " +
                        $"(known: {string.Join(", ", domains.KnownNames)})");

        var requested = ParseSlices(slicesArg);
        if (requested.UnknownSlices.Count > 0)
            return Text($"domain-info failed: unknown slice(s) '{string.Join(", ", requested.UnknownSlices)}' " +
                        $"(known: {string.Join(", ", AllSliceNames)}, all)");

        var projection = Project(domain!, requested.Selected, artefacts);
        var json = JsonSerializer.Serialize(projection, JsonOpts);
        return Text(json);
    }

    private static IResult Text(string text) => Results.Text(text, "text/plain");

    // ------------------------------------------------------ slice plumbing

    private static readonly string[] AllSliceNames = new[]
    {
        "name", "plan-files", "commits", "governed-globs",
        "playbook-path", "glossary", "business-rules-path",
        "trusted-references",
        "artefacts",  // Plan-11: projection from IArtefactRegistry by owner.
    };

    private sealed record SliceRequest(IReadOnlySet<string> Selected, IReadOnlyList<string> UnknownSlices);

    private static SliceRequest ParseSlices(string slicesArg)
    {
        if (slicesArg == "all" || slicesArg == "*")
            return new SliceRequest(new HashSet<string>(AllSliceNames), Array.Empty<string>());

        var requested = slicesArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var name in requested)
        {
            if (Array.IndexOf(AllSliceNames, name) >= 0)
                selected.Add(name);
            else
                unknown.Add(name);
        }
        return new SliceRequest(selected, unknown);
    }

    private static Dictionary<string, object?> Project(IDomain d, IReadOnlySet<string> wanted, IArtefactRegistry artefacts)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["domain"] = d.Name,
        };

        if (wanted.Contains("name"))               result["name"] = d.Name;
        if (wanted.Contains("plan-files"))         result["plan-files"] = new
        {
            prefix       = d.PlanFiles.Prefix,
            number_floor = d.PlanFiles.NumberFloor,
            suffix       = d.PlanFiles.Suffix,
            directory    = d.PlanFiles.Directory,
        };
        if (wanted.Contains("commits"))            result["commits"] = d.Commits.Prefixes
            .ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        if (wanted.Contains("governed-globs"))     result["governed-globs"] = d.GovernedGlobs;
        if (wanted.Contains("playbook-path"))      result["playbook-path"] = d.PlaybookPath;
        if (wanted.Contains("glossary"))           result["glossary"] = d.Glossary;
        if (wanted.Contains("business-rules-path")) result["business-rules-path"] = d.BusinessRulesPath;
        if (wanted.Contains("trusted-references")) result["trusted-references"] = d.TrustedReferences
            .Select(r => new
            {
                title          = r.Title,
                url            = r.Url.ToString(),
                why            = r.Why,
                added_on       = r.AddedOn?.ToString("yyyy-MM-dd"),
                added_in_plan  = r.AddedInPlan,
            })
            .ToArray();

        // Plan-11: artefacts is a PROJECTION from IArtefactRegistry — not a
        // property on IDomain. Domains stay declarative-knowledge facades;
        // artefacts stay runtime machinery; the composition happens here.
        if (wanted.Contains("artefacts")) result["artefacts"] = artefacts
            .ByOwner(d.Name)
            .Select(s => new
            {
                name             = s.Name,
                key_template     = s.KeyTemplate,
                schema_name      = s.SchemaName,
                schema_version   = s.SchemaVersion,
                lifecycle        = s.Lifecycle.ToString(),
                git_tracked      = s.GitTracked,
                producer         = s.Producer,
                governing_br     = s.GoverningBR,
                cost_class       = s.CostClass.ToString(),
                destinations     = s.Destinations.Select(dr => new
                {
                    name       = dr.DestinationName,
                    on_failure = dr.OnFailure.ToString(),
                }).ToArray(),
            })
            .ToArray();

        return result;
    }
}
