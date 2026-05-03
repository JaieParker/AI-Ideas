using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-PROCESS-009 — gate check. The check passes iff every
/// ARCHITECTURE_DECISION_REQUIRED block in the plan body has a
/// matching resolution in the "## Architecture review decisions"
/// section.
/// </summary>
public class ArchitectureReviewGateCheckerTests
{
    [Fact(DisplayName = "BR-PROCESS-009 — empty body passes (nothing to gate)")]
    public void Empty_Body_Passes()
    {
        var result = ArchitectureReviewGateChecker.Check(string.Empty);
        Assert.True(result.Pass);
        Assert.Empty(result.UnresolvedCommitments);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — body with no ARCHITECTURE_DECISION_REQUIRED markers passes")]
    public void No_Markers_Passes()
    {
        var body = """
            # Plan
            ## Motivation
            All compatible; no EXTENDS rows.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.True(result.Pass);
        Assert.Empty(result.UnresolvedCommitments);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — markers without a decisions section fail")]
    public void Markers_Without_Section_Fail()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-PROCESS-008
              current:    each tier owns its lifecycle
              proposed:   centralised lifecycle skill
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.False(result.Pass);
        Assert.Contains("BR-PROCESS-008", result.UnresolvedCommitments);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — markers with matching Evolve resolution pass")]
    public void Markers_With_Evolve_Pass()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-PROCESS-008
              current:    each tier owns its lifecycle
              proposed:   centralised lifecycle skill

            ## Architecture review decisions

            - BR-PROCESS-008 (each tier owns its lifecycle): **Resolution: Evolve** — amend the rule to allow centralised lifecycle for cross-tier orchestration.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.True(result.Pass);
        Assert.Empty(result.UnresolvedCommitments);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — Override resolution counts as resolved")]
    public void Override_Counts_As_Resolved()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-CODE-001
              current:    no magic strings
              proposed:   single hardcoded URL acceptable here

            ## Architecture review decisions

            - BR-CODE-001 (no magic strings): **Resolution: Override** — one-off for the bootstrap probe URL; tracked in process-incidents.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.True(result.Pass);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — Constrain and Defer also count as resolved")]
    public void Constrain_And_Defer_Count_As_Resolved()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-PROCESS-008

            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-CODE-001

            ## Architecture review decisions

            - BR-PROCESS-008 (...): **Resolution: Constrain** — rework the plan to stay within the rule.
            - BR-CODE-001 (...): **Resolution: Defer** — capture the question as an open architectural item.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.True(result.Pass);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — partial resolution fails with the missing commitments listed")]
    public void Partial_Resolution_Fails()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-PROCESS-008

            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-CODE-001

            ## Architecture review decisions

            - BR-PROCESS-008 (...): **Resolution: Evolve** — amend the rule.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.False(result.Pass);
        Assert.Single(result.UnresolvedCommitments);
        Assert.Contains("BR-CODE-001", result.UnresolvedCommitments);
        Assert.DoesNotContain("BR-PROCESS-008", result.UnresolvedCommitments);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — invalid resolution word does not count as resolved")]
    public void Invalid_Resolution_Word_Fails()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-PROCESS-008

            ## Architecture review decisions

            - BR-PROCESS-008 (...): **Resolution: Maybe** — not a valid choice.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.False(result.Pass);
        Assert.Contains("BR-PROCESS-008", result.UnresolvedCommitments);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — decisions section ends at the next ## heading")]
    public void Decisions_Section_Ends_At_Next_Heading()
    {
        var body = """
            ARCHITECTURE_DECISION_REQUIRED:
              commitment: BR-PROCESS-008

            ## Architecture review decisions

            (no resolutions yet)

            ## Rollback steps

            - BR-PROCESS-008 (...): **Resolution: Evolve** — this resolution is in the WRONG section and should NOT count.
            """;
        var result = ArchitectureReviewGateChecker.Check(body);
        Assert.False(result.Pass);
        Assert.Contains("BR-PROCESS-008", result.UnresolvedCommitments);
    }
}
