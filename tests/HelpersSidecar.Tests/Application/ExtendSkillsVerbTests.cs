using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-EXTEND-002 / BR-PROCESS-001 / BR-EXTEND-007 — /extend-skills
/// verb parsing. Args shape: <c>&lt;domain&gt; [&lt;topic&gt; | revert | status]</c>.
/// </summary>
public class ExtendSkillsVerbTests
{
    [Fact(DisplayName = "BR-EXTEND-007 — empty args is UsageMissingDomain")]
    public void Empty_Is_UsageMissingDomain()
    {
        var v = ExtendSkillsVerb.Parse("");
        Assert.Equal(ExtendSkillsVerbKind.UsageMissingDomain, v.Kind);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — single token is the domain with Begin (no topic)")]
    public void Domain_Only_Is_Begin_With_No_Topic()
    {
        var v = ExtendSkillsVerb.Parse("otel");
        Assert.Equal(ExtendSkillsVerbKind.Begin, v.Kind);
        Assert.Equal("otel", v.Domain);
        Assert.Null(v.Topic);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — domain + revert is Revert verb")]
    public void Domain_Revert()
    {
        var v = ExtendSkillsVerb.Parse("otel revert");
        Assert.Equal(ExtendSkillsVerbKind.Revert, v.Kind);
        Assert.Equal("otel", v.Domain);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — domain + status is Status verb")]
    public void Domain_Status()
    {
        var v = ExtendSkillsVerb.Parse("otel status");
        Assert.Equal(ExtendSkillsVerbKind.Status, v.Kind);
        Assert.Equal("otel", v.Domain);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — domain + free-form text is Begin with topic = full remainder")]
    public void Domain_With_Topic_Is_Begin()
    {
        var v = ExtendSkillsVerb.Parse("otel fix the foo bar");
        Assert.Equal(ExtendSkillsVerbKind.Begin, v.Kind);
        Assert.Equal("otel", v.Domain);
        Assert.Equal("fix the foo bar", v.Topic);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — domain + single-word topic still Begin")]
    public void Domain_With_Single_Word_Topic()
    {
        var v = ExtendSkillsVerb.Parse("otel foo");
        Assert.Equal(ExtendSkillsVerbKind.Begin, v.Kind);
        Assert.Equal("otel", v.Domain);
        Assert.Equal("foo", v.Topic);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — domain is the FIRST token only; 'otel' as a topic-word doesn't override")]
    public void Domain_Is_First_Token_Only()
    {
        var v = ExtendSkillsVerb.Parse("kai-platform refactor otel-something");
        Assert.Equal(ExtendSkillsVerbKind.Begin, v.Kind);
        Assert.Equal("kai-platform", v.Domain);
        Assert.Equal("refactor otel-something", v.Topic);
    }

    [Fact(DisplayName = "BR-EXTEND-007 — `revert` only triggers when it's the second token (after the domain)")]
    public void Revert_Is_Second_Token()
    {
        var v = ExtendSkillsVerb.Parse("otel revert-an-earlier-change");
        // 'revert-an-earlier-change' is NOT the bare 'revert' verb — it's a topic.
        Assert.Equal(ExtendSkillsVerbKind.Begin, v.Kind);
        Assert.Equal("otel", v.Domain);
        Assert.Equal("revert-an-earlier-change", v.Topic);
    }
}
