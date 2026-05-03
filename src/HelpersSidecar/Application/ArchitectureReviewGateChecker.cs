using System.Text.RegularExpressions;

namespace HelpersSidecar.Application;

/// <summary>
/// Deterministic check for BR-PROCESS-009: every
/// ARCHITECTURE_DECISION_REQUIRED block produced by
/// /architecture-review must have a matching resolution
/// (Evolve / Constrain / Defer / Override) recorded in the plan
/// file's "## Architecture review decisions" section before
/// Phase 2 (Implement) is allowed to proceed.
///
/// Per BR-SKILL-006 this is a deterministic helper — pure-data,
/// no LLM. The check counts and matches; it does NOT evaluate
/// whether a resolution is correct (that's the human's job per
/// BR-PROCESS-009).
/// </summary>
public static class ArchitectureReviewGateChecker
{
    private static readonly Regex DecisionRequiredRegex = new(
        @"ARCHITECTURE_DECISION_REQUIRED:\s*\n\s*commitment:\s*(BR-[A-Z]+-\d+)",
        RegexOptions.Compiled);

    private static readonly Regex DecisionsSectionRegex = new(
        @"##\s+Architecture review decisions\s*\n",
        RegexOptions.Compiled);

    private static readonly Regex ResolutionLineRegex = new(
        @"-\s+(BR-[A-Z]+-\d+).*\*\*Resolution:\s*(Evolve|Constrain|Defer|Override)\*\*",
        RegexOptions.Compiled);

    /// <summary>
    /// Scan <paramref name="planFileBody"/> and return whether the gate
    /// passes. The gate passes when:
    /// <list type="bullet">
    ///   <item>The plan has no ARCHITECTURE_DECISION_REQUIRED blocks (nothing to gate), OR</item>
    ///   <item>Every BR-ID named in an ARCHITECTURE_DECISION_REQUIRED block has a
    ///         corresponding resolution line (BR-ID + bold Resolution: word) in
    ///         the "## Architecture review decisions" section.</item>
    /// </list>
    /// </summary>
    public static GateResult Check(string planFileBody)
    {
        if (string.IsNullOrEmpty(planFileBody))
            return new GateResult(Pass: true, Reason: "empty plan body — nothing to gate", UnresolvedCommitments: Array.Empty<string>());

        var requiredCommitments = DecisionRequiredRegex
            .Matches(planFileBody)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        if (requiredCommitments.Count == 0)
            return new GateResult(Pass: true, Reason: "no ARCHITECTURE_DECISION_REQUIRED markers in plan", UnresolvedCommitments: Array.Empty<string>());

        var sectionMatch = DecisionsSectionRegex.Match(planFileBody);
        if (!sectionMatch.Success)
            return new GateResult(
                Pass: false,
                Reason: $"plan has {requiredCommitments.Count} ARCHITECTURE_DECISION_REQUIRED marker(s) but no '## Architecture review decisions' section",
                UnresolvedCommitments: requiredCommitments.OrderBy(s => s, StringComparer.Ordinal).ToArray());

        var sectionStart = sectionMatch.Index + sectionMatch.Length;
        var nextSectionMatch = Regex.Match(planFileBody[sectionStart..], @"\n##\s+", RegexOptions.Compiled);
        var sectionBody = nextSectionMatch.Success
            ? planFileBody.Substring(sectionStart, nextSectionMatch.Index)
            : planFileBody[sectionStart..];

        var resolved = ResolutionLineRegex
            .Matches(sectionBody)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var unresolved = requiredCommitments.Except(resolved)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        if (unresolved.Length == 0)
            return new GateResult(
                Pass: true,
                Reason: $"all {requiredCommitments.Count} ARCHITECTURE_DECISION_REQUIRED commitment(s) have matching resolutions",
                UnresolvedCommitments: Array.Empty<string>());

        return new GateResult(
            Pass: false,
            Reason: $"{unresolved.Length} of {requiredCommitments.Count} ARCHITECTURE_DECISION_REQUIRED commitment(s) lack a resolution",
            UnresolvedCommitments: unresolved);
    }
}

public sealed record GateResult(bool Pass, string Reason, IReadOnlyList<string> UnresolvedCommitments);
