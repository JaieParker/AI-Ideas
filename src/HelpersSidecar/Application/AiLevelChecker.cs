using System.Text.RegularExpressions;

namespace HelpersSidecar.Application;

/// <summary>
/// Runs the deterministic half of the 4 D rubric (BR-SKILL-013)
/// against a parsed <see cref="SkillFile"/>. Each dimension's
/// final score is the minimum of its checks: any failure →
/// Absent (if the check carried a Strong contribution) or
/// Partial (if a tie); all-pass → Strong.
///
/// <para>The judgement half — does the description disambiguate,
/// does the body enable verification, etc. — is NOT in this
/// class. Per BR-SKILL-006 / BR-SKILL-012 the sidecar runs only
/// the deterministic checks; the renderer composes a prompt the
/// in-session Claude reads to score the judgement dimensions.</para>
/// </summary>
public sealed class AiLevelChecker
{
    private static readonly Regex BrCitationRegex = new(@"BR-[A-Z]+-\d{3}", RegexOptions.Compiled);
    private static readonly Regex SchemaVersionRegex = new(@"\b[A-Z][A-Z_]+\s+v\d+\b", RegexOptions.Compiled);
    private static readonly Regex DurabilityHintRegex = new(@"\b(curl\s+http://127\.0\.0\.1|output/|report\b|commit\b)", RegexOptions.Compiled);
    private static readonly Regex ReversalHintRegex = new(@"\b(revert|discard|down|unset|undo|clear|stop)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DelegationCalloutRegex = new(@"\b(deterministic|judgement|judgment|sidecar handles|claude (handles|drives|analyses|analyzes))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int MinDescriptionLength = 50;

    public AiLevelSkillScore Score(SkillFile skill)
    {
        var dimensionScores = Enum.GetValues<AiLevelDimension>()
            .Select(d => ScoreDimension(d, skill))
            .ToList();

        var total = dimensionScores.Sum(d => (int)d.Score);
        return new AiLevelSkillScore(skill.Name, dimensionScores, total);
    }

    private AiLevelDimensionScore ScoreDimension(AiLevelDimension dimension, SkillFile skill)
    {
        var outcomes = ChecksFor(dimension)
            .Select(check =>
            {
                var r = check.Run(skill);
                return new AiLevelCheckOutcome(check.Name, r.Passed, r.Evidence);
            })
            .ToList();

        // Scoring rule: all checks pass → Strong; some pass → Partial; none → Absent.
        var passed = outcomes.Count(o => o.Passed);
        var score = passed == 0 ? AiLevelScore.Absent
                  : passed == outcomes.Count ? AiLevelScore.Strong
                  : AiLevelScore.Partial;

        return new AiLevelDimensionScore(dimension, score, outcomes);
    }

    public IReadOnlyList<AiLevelCheck> ChecksFor(AiLevelDimension dimension) =>
        dimension switch
        {
            AiLevelDimension.Delegation   => DelegationChecks,
            AiLevelDimension.Description  => DescriptionChecks,
            AiLevelDimension.Discernment  => DiscernmentChecks,
            AiLevelDimension.Diligence    => DiligenceChecks,
            _ => Array.Empty<AiLevelCheck>(),
        };

    // ---------------- Delegation ----------------
    private static readonly IReadOnlyList<AiLevelCheck> DelegationChecks = new[]
    {
        new AiLevelCheck(AiLevelDimension.Delegation, "model-invocation-explicit",
            "disable-model-invocation set explicitly (true or false)",
            AiLevelScore.Strong,
            sf => sf.Frontmatter.TryGetValue("disable-model-invocation", out var v) && (v == "true" || v == "false")
                ? new AiLevelCheckResult(true, $"disable-model-invocation: {v}")
                : new AiLevelCheckResult(false, "disable-model-invocation not explicit in frontmatter")),

        new AiLevelCheck(AiLevelDimension.Delegation, "allowed-tools-tight-prefix",
            "allowed-tools uses the tightest viable prefix (no bare Bash/Skill/curl-wildcard)",
            AiLevelScore.Strong,
            sf => CheckAllowedToolsTightness(sf)),

        new AiLevelCheck(AiLevelDimension.Delegation, "delegation-callout-in-body",
            "body explicitly calls out deterministic vs judgement work",
            AiLevelScore.Strong,
            sf => DelegationCalloutRegex.IsMatch(sf.Body)
                ? new AiLevelCheckResult(true, "delegation language present in body")
                : new AiLevelCheckResult(false, "no deterministic/judgement language found in body")),
    };

    // ---------------- Description ----------------
    private static readonly IReadOnlyList<AiLevelCheck> DescriptionChecks = new[]
    {
        new AiLevelCheck(AiLevelDimension.Description, "description-length",
            $"description field present and ≥ {MinDescriptionLength} characters",
            AiLevelScore.Strong,
            sf => sf.Frontmatter.TryGetValue("description", out var v) && v.Length >= MinDescriptionLength
                ? new AiLevelCheckResult(true, $"description: {v.Length} chars")
                : new AiLevelCheckResult(false, sf.Frontmatter.ContainsKey("description")
                    ? $"description present but only {sf.Frontmatter["description"].Length} chars (< {MinDescriptionLength})"
                    : "description field absent")),

        new AiLevelCheck(AiLevelDimension.Description, "argument-hint-present",
            "argument-hint field present in frontmatter",
            AiLevelScore.Strong,
            sf => sf.Frontmatter.ContainsKey("argument-hint")
                ? new AiLevelCheckResult(true, $"argument-hint: {sf.Frontmatter["argument-hint"]}")
                : new AiLevelCheckResult(false, "argument-hint field absent")),

        new AiLevelCheck(AiLevelDimension.Description, "allowed-tools-structured",
            "allowed-tools field present (structured, not implicit)",
            AiLevelScore.Strong,
            sf => sf.Frontmatter.TryGetValue("allowed-tools", out var v) && !string.IsNullOrWhiteSpace(v)
                ? new AiLevelCheckResult(true, "allowed-tools listed")
                : new AiLevelCheckResult(false, "allowed-tools field absent or empty")),
    };

    // ---------------- Discernment ----------------
    private static readonly IReadOnlyList<AiLevelCheck> DiscernmentChecks = new[]
    {
        new AiLevelCheck(AiLevelDimension.Discernment, "br-citations-in-body",
            "body cites at least one BR-<AREA>-NN identifier",
            AiLevelScore.Strong,
            sf => BrCitationRegex.IsMatch(sf.Body)
                ? new AiLevelCheckResult(true, $"BR citations: {BrCitationRegex.Matches(sf.Body).Count}")
                : new AiLevelCheckResult(false, "no BR-<AREA>-NN citations found in body")),

        new AiLevelCheck(AiLevelDimension.Discernment, "schema-version-marker",
            "body emits a schema-version marker (<NAME> v<N>)",
            AiLevelScore.Strong,
            sf => SchemaVersionRegex.IsMatch(sf.Body)
                ? new AiLevelCheckResult(true, "schema-version marker present in body")
                : new AiLevelCheckResult(false, "no schema-version marker found in body")),
    };

    // ---------------- Diligence ----------------
    private static readonly IReadOnlyList<AiLevelCheck> DiligenceChecks = new[]
    {
        new AiLevelCheck(AiLevelDimension.Diligence, "durability-hint",
            "body indicates durable artefact production (curl, output/, report, commit)",
            AiLevelScore.Strong,
            sf => DurabilityHintRegex.IsMatch(sf.Body)
                ? new AiLevelCheckResult(true, "durability hint present in body")
                : new AiLevelCheckResult(false, "no durability hint found in body")),

        new AiLevelCheck(AiLevelDimension.Diligence, "reversal-hint",
            "body names an inverse / undo path (revert, discard, down, unset, undo, clear, stop)",
            AiLevelScore.Strong,
            sf => ReversalHintRegex.IsMatch(sf.Body)
                ? new AiLevelCheckResult(true, "reversal hint present in body")
                : new AiLevelCheckResult(false, "no reversal hint found in body")),
    };

    private static AiLevelCheckResult CheckAllowedToolsTightness(SkillFile sf)
    {
        if (!sf.Frontmatter.TryGetValue("allowed-tools", out var raw) || string.IsNullOrWhiteSpace(raw))
            return new AiLevelCheckResult(false, "allowed-tools missing");

        // Anti-patterns from BR-SKILL-009.
        var bad = new[]
        {
            "Bash(*)", "Bash *", "Bash )", "Skill(*)",
            "Bash(curl *)", "Bash(curl http://*)", "Bash(curl https://*)",
        };
        foreach (var pattern in bad)
        {
            if (raw.Contains(pattern, StringComparison.Ordinal))
                return new AiLevelCheckResult(false, $"loose pattern detected: '{pattern}'");
        }

        // Bare 'Skill' (no args following) is also too broad.
        if (raw.Contains(" Skill ", StringComparison.Ordinal) || raw.EndsWith(" Skill", StringComparison.Ordinal) || raw == "Skill")
            return new AiLevelCheckResult(false, "bare 'Skill' permission detected");

        return new AiLevelCheckResult(true, "no loose patterns detected");
    }
}
