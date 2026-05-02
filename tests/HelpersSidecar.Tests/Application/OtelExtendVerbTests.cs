using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>BR-EXTEND-002 / BR-PROCESS-001 — /otel-extend verb parsing.</summary>
public class OtelExtendVerbTests
{
    [Fact(DisplayName = "BR-PROCESS-001 — empty args is Begin (with no topic)")]
    public void Empty_Is_Begin_With_No_Topic()
    {
        var v = OtelExtendVerb.Parse("");
        Assert.Equal(OtelExtendVerbKind.Begin, v.Kind);
        Assert.Null(v.Topic);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `revert` is its own verb")]
    public void Revert_Verb()
    {
        var v = OtelExtendVerb.Parse("revert");
        Assert.Equal(OtelExtendVerbKind.Revert, v.Kind);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — `status` is its own verb")]
    public void Status_Verb()
    {
        var v = OtelExtendVerb.Parse("status");
        Assert.Equal(OtelExtendVerbKind.Status, v.Kind);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — anything else is Begin with the full string as topic")]
    public void Free_Form_Is_Begin_With_Topic()
    {
        var v = OtelExtendVerb.Parse("fix the foo bar");
        Assert.Equal(OtelExtendVerbKind.Begin, v.Kind);
        Assert.Equal("fix the foo bar", v.Topic);
    }

    [Fact(DisplayName = "BR-PROCESS-001 — single-word topic still Begin (not a sub-verb)")]
    public void Single_Word_Topic()
    {
        var v = OtelExtendVerb.Parse("foo");
        Assert.Equal(OtelExtendVerbKind.Begin, v.Kind);
        Assert.Equal("foo", v.Topic);
    }
}
