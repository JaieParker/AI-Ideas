using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>BR-SKILL-007 — /enrich verb parsing (sidecar owns dispatch).</summary>
public class EnrichVerbTests
{
    [Fact(DisplayName = "BR-SKILL-007 — empty args returns Usage")]
    public void Empty_Returns_Usage() =>
        Assert.Equal(EnrichVerbKind.Usage, EnrichVerb.Parse("").Kind);

    [Theory(DisplayName = "BR-SKILL-007 — flag verbs parse")]
    [InlineData("--show", EnrichVerbKind.Show)]
    [InlineData("--clear", EnrichVerbKind.Clear)]
    public void Flag_Verbs_Parse(string args, EnrichVerbKind expected)
    {
        Assert.Equal(expected, EnrichVerb.Parse(args).Kind);
    }

    [Fact(DisplayName = "BR-SKILL-007 — --remove with key parses; bare --remove is Usage")]
    public void Remove_Parsing()
    {
        var ok = EnrichVerb.Parse("--remove ticket.id");
        Assert.Equal(EnrichVerbKind.Remove, ok.Kind);
        Assert.Equal("ticket.id", ok.Key);

        Assert.Equal(EnrichVerbKind.Usage, EnrichVerb.Parse("--remove").Kind);
    }

    [Fact(DisplayName = "BR-SKILL-007 — key + single-token value parses")]
    public void Key_Value_Parses()
    {
        var v = EnrichVerb.Parse("ticket.id PROJ-1234");
        Assert.Equal(EnrichVerbKind.Set, v.Kind);
        Assert.Equal("ticket.id", v.Key);
        Assert.Equal("PROJ-1234", v.Value);
    }

    [Fact(DisplayName = "BR-SKILL-007 — value can contain spaces (everything after first whitespace)")]
    public void Value_Can_Contain_Spaces()
    {
        var v = EnrichVerb.Parse("feature payment rewrite v2");
        Assert.Equal(EnrichVerbKind.Set, v.Kind);
        Assert.Equal("feature", v.Key);
        Assert.Equal("payment rewrite v2", v.Value);
    }

    [Fact(DisplayName = "BR-SKILL-007 — key without value is Usage")]
    public void Key_Without_Value_Is_Usage() =>
        Assert.Equal(EnrichVerbKind.Usage, EnrichVerb.Parse("ticket.id").Kind);

    [Fact(DisplayName = "BR-SKILL-007 — unknown flag is Usage")]
    public void Unknown_Flag_Is_Usage() =>
        Assert.Equal(EnrichVerbKind.Usage, EnrichVerb.Parse("--bogus").Kind);
}
