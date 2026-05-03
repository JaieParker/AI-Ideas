using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Application;

/// <summary>
/// Composes the prompt context that <see cref="HelpersSidecar.Endpoints.ArchitectureReviewDispatchEndpoint"/>
/// renders for Claude (the user's session) to read.
///
/// Per BR-SKILL-012 the dispatch endpoint is a context loader and
/// prompt renderer; the analyst is Claude. This class reads the
/// inputs (CLAUDE.md, business-rules.md, recent plans, the target
/// body, the resolved domain's slices including TrustedReferences)
/// and packs them into a single text block with clear section
/// headers.
///
/// Phase 2a: skeleton only — assembles the context shape and exposes
/// the schema document. Integration with /extend-skills (Phase 1.5
/// gate) and the live dispatch endpoint follows in later phases.
/// </summary>
public sealed class ArchitectureReviewContextLoader
{
    private readonly IDomainResolver _domains;
    private readonly string _projectRoot;

    public ArchitectureReviewContextLoader(IDomainResolver domains, string projectRoot)
    {
        _domains = domains;
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// Build the prompt body Claude reads. The output is a multi-section
    /// markdown-flavoured text block:
    ///
    /// <list type="number">
    ///   <item>Header (target, domain, schema version)</item>
    ///   <item>CLAUDE.md (architectural commitments)</item>
    ///   <item>docs/business-rules.md (full rule register)</item>
    ///   <item>docs/process-incidents.md (priors)</item>
    ///   <item>Recent plan files (last N for context on prior decisions)</item>
    ///   <item>Domain slices: Glossary, GovernedGlobs, BusinessRulesPath,
    ///     TrustedReferences (citation allow-list)</item>
    ///   <item>Target body (the file under review)</item>
    ///   <item>The response schema (verbatim)</item>
    /// </list>
    /// </summary>
    public ArchitectureReviewContext Build(string target, string? domainName, string? planTag = null)
    {
        var domain = _domains.ResolveOrThrow(domainName ?? "otel");
        var targetBody = TryReadFromRoot(target);
        var businessRules = TryReadFromRoot("docs/business-rules.md");
        var claudeMd = TryReadFromRoot("CLAUDE.md");
        var processIncidents = TryReadFromRoot("docs/process-incidents.md");
        var recentPlans = ListRecentPlans(domain, count: 3);

        return new ArchitectureReviewContext(
            Target: target,
            Domain: domain,
            PlanTag: planTag,
            ClaudeMd: claudeMd,
            BusinessRules: businessRules,
            ProcessIncidents: processIncidents,
            RecentPlans: recentPlans,
            TargetBody: targetBody,
            Schema: ResponseSchema);
    }

    private string TryReadFromRoot(string relativePath)
    {
        var full = Path.Combine(_projectRoot, relativePath);
        try { return File.Exists(full) ? File.ReadAllText(full) : string.Empty; }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    private IReadOnlyList<(string Name, string Body)> ListRecentPlans(IDomain domain, int count)
    {
        // Plan-9: read recent plans from the domain's PlanFiles.Directory.
        // Directory == "." preserves the pre-Plan-9 behaviour (project root).
        var planDir = domain.PlanFiles.Directory == "."
            ? _projectRoot
            : Path.Combine(_projectRoot, domain.PlanFiles.Directory);

        try
        {
            if (!Directory.Exists(planDir))
                return Array.Empty<(string, string)>();

            var planFiles = Directory.GetFiles(planDir, "*.md", SearchOption.TopDirectoryOnly)
                .Where(f => domain.PlanFiles.TryParse(Path.GetFileName(f), out _, out _))
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.Name, StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .Select(fi => (fi.Name, File.ReadAllText(fi.FullName)))
                .ToList();
            return planFiles;
        }
        catch (IOException) { return Array.Empty<(string, string)>(); }
    }

    /// <summary>
    /// The ARCHITECTURE_REVIEW v1 schema (BR-PROCESS-009) the dispatch
    /// endpoint embeds verbatim into the prompt.
    /// </summary>
    public const string ResponseSchema = """
        ARCHITECTURE_REVIEW v1
        ======================
        Target:        <what was reviewed (path or plan-id)>
        Date:          <UTC timestamp>
        Domain:        <name from IDomainResolver>
        Plan tag:      <value of session enrichment "plan", if any>
        Reviewer:      Claude (model: <model id>; session: <session id>)

        PER-COMMITMENT EVALUATION
        -------------------------
        <For every BR-* in scope of the change>:
          <BR-ID> (<short title>)
            STATUS:    COMPATIBLE | VIOLATES | EXTENDS
            REASONING: <≤3 sentences>
            CITED:     <0..N URLs from the domain's TrustedReferences;
                        use "(none)" when no external citation applies>

        ARCHITECTURAL DECISIONS REQUIRED
        --------------------------------
        <None | one or more entries of the form:>
          ARCHITECTURE_DECISION_REQUIRED:
            commitment: <BR-ID>
            current:    <one-sentence summary of the existing rule>
            proposed:   <one-sentence summary of how the change extends it>
            options:
              Evolve    — amend the BR text and consequent CLAUDE.md sections.
              Constrain — rework the plan to stay within the current rule.
              Defer     — capture as open question; do not land this change yet.
              Override  — accept the deviation as a one-off with one-line
                          justification recorded in the plan.

        OUT-OF-SCOPE CONCERNS
        ---------------------
        <Any qualitative observations the reviewer raised that no current
         BR covers. Each is one paragraph; flag with QC: prefix.>

        RECOMMENDATION
        --------------
        PROCEED | EVOLVE_FIRST | CONSTRAIN | DEFER | DISCUSS

        REVIEWER NOTES (optional)
        -------------------------
        <Free-form. May reference TrustedReferences. May include suggested
         BR text amendments if the reviewer feels strongly about an Evolve.>
        """;
}

public sealed record ArchitectureReviewContext(
    string Target,
    IDomain Domain,
    string? PlanTag,
    string ClaudeMd,
    string BusinessRules,
    string ProcessIncidents,
    IReadOnlyList<(string Name, string Body)> RecentPlans,
    string TargetBody,
    string Schema);
