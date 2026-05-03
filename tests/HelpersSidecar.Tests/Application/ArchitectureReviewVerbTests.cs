using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-PROCESS-009 / BR-SKILL-012 — /architecture-review verb parsing.
/// Args shape: <c>&lt;target&gt; [--domain=&lt;name&gt;]</c>.
/// </summary>
public class ArchitectureReviewVerbTests
{
    [Fact(DisplayName = "BR-PROCESS-009 — empty args returns no-target verb")]
    public void Empty_Returns_No_Target()
    {
        var v = ArchitectureReviewVerb.Parse("");
        Assert.False(v.HasTarget);
        Assert.Equal(string.Empty, v.Target);
        Assert.Null(v.Domain);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — target-only sets the target; domain stays null (caller defaults)")]
    public void Target_Only()
    {
        var v = ArchitectureReviewVerb.Parse("The-OTEL-Plan-7-foo.md");
        Assert.True(v.HasTarget);
        Assert.Equal("The-OTEL-Plan-7-foo.md", v.Target);
        Assert.Null(v.Domain);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — --domain= flag captures the domain name")]
    public void Domain_Override()
    {
        var v = ArchitectureReviewVerb.Parse("The-OTEL-Plan-7-foo.md --domain=otel");
        Assert.Equal("The-OTEL-Plan-7-foo.md", v.Target);
        Assert.Equal("otel", v.Domain);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — flag may precede target")]
    public void Domain_Before_Target()
    {
        var v = ArchitectureReviewVerb.Parse("--domain=otel some-target.md");
        Assert.Equal("some-target.md", v.Target);
        Assert.Equal("otel", v.Domain);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — 'current' is a valid target shape (uncommitted diff)")]
    public void Current_Is_Valid_Target()
    {
        var v = ArchitectureReviewVerb.Parse("current");
        Assert.True(v.HasTarget);
        Assert.Equal("current", v.Target);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — first non-flag token wins as target; later tokens are ignored")]
    public void First_Non_Flag_Token_Wins()
    {
        var v = ArchitectureReviewVerb.Parse("plan-7 plan-6");
        Assert.Equal("plan-7", v.Target);
    }

    [Fact(DisplayName = "BR-PROCESS-009 — --domain= with empty value yields null Domain")]
    public void Empty_Domain_Value_Is_Null()
    {
        var v = ArchitectureReviewVerb.Parse("target.md --domain=");
        Assert.Equal("target.md", v.Target);
        Assert.Null(v.Domain);
    }
}
