using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-SKILL-007 — /otel verb parsing.
/// Coverage spans every verb in HELP.md including the multi-key get
/// (BR-ENRICH-014) and the colon-separated set (the documented
/// /otel set <key>:<value> form).
/// </summary>
public class OtelVerbTests
{
    [Fact(DisplayName = "BR-SKILL-007 — empty args is Setup (bootstrap-and-status)")]
    public void Empty_Is_Setup() =>
        Assert.Equal(OtelVerbKind.Setup, OtelVerb.Parse("").Kind);

    [Theory(DisplayName = "BR-SKILL-007 — leaf verbs parse")]
    [InlineData("on",      OtelVerbKind.On)]
    [InlineData("off",     OtelVerbKind.Off)]
    [InlineData("status",  OtelVerbKind.Status)]
    [InlineData("restart", OtelVerbKind.Restart)]
    [InlineData("help",    OtelVerbKind.Help)]
    [InlineData("config",  OtelVerbKind.Config)]
    [InlineData("up",      OtelVerbKind.Up)]
    [InlineData("down",    OtelVerbKind.Down)]
    public void Leaf_Verbs_Parse(string s, OtelVerbKind expected) =>
        Assert.Equal(expected, OtelVerb.Parse(s).Kind);

    [Fact(DisplayName = "BR-OTEL-006 — /otel up captures optional config-file argument")]
    public void Up_Captures_Config_File_Argument()
    {
        var v = OtelVerb.Parse("up config.alt.yaml");
        Assert.Equal(OtelVerbKind.Up, v.Kind);
        Assert.Equal("config.alt.yaml", v.ConfigFile);
    }

    [Fact(DisplayName = "BR-OTEL-006 — /otel up with no argument has null ConfigFile")]
    public void Up_Without_Arg_Has_Null_ConfigFile()
    {
        var v = OtelVerb.Parse("up");
        Assert.Equal(OtelVerbKind.Up, v.Kind);
        Assert.Null(v.ConfigFile);
    }

    [Fact(DisplayName = "BR-SKILL-007 — config clear is its own verb")]
    public void Config_Clear() =>
        Assert.Equal(OtelVerbKind.ConfigClear, OtelVerb.Parse("config clear").Kind);

    [Fact(DisplayName = "BR-SKILL-007 — set parses key:value form")]
    public void Set_Colon_Form()
    {
        var v = OtelVerb.Parse("set team:platform");
        Assert.Equal(OtelVerbKind.Set, v.Kind);
        Assert.Equal("team", v.Key);
        Assert.Equal("platform", v.Value);
    }

    [Theory(DisplayName = "BR-SKILL-007 — set without value or without colon is Usage")]
    [InlineData("set")]
    [InlineData("set team")]
    [InlineData("set :value")]
    [InlineData("set team:")]
    public void Set_Bad_Forms_Are_Usage(string s) =>
        Assert.Equal(OtelVerbKind.Usage, OtelVerb.Parse(s).Kind);

    [Fact(DisplayName = "BR-ENRICH-013 — get with one key is single-key form")]
    public void Get_Single_Key()
    {
        var v = OtelVerb.Parse("get team");
        Assert.Equal(OtelVerbKind.Get, v.Kind);
        Assert.Equal("team", v.Key);
    }

    [Fact(DisplayName = "BR-ENRICH-014 — get with multiple keys is bulk form")]
    public void Get_Multiple_Keys()
    {
        var v = OtelVerb.Parse("get team env cost.center");
        Assert.Equal(OtelVerbKind.GetMany, v.Kind);
        Assert.NotNull(v.Keys);
        Assert.Equal(new[] { "team", "env", "cost.center" }, v.Keys);
    }

    [Fact(DisplayName = "BR-SKILL-007 — bare get is Usage")]
    public void Bare_Get_Is_Usage() =>
        Assert.Equal(OtelVerbKind.Usage, OtelVerb.Parse("get").Kind);

    [Fact(DisplayName = "BR-SKILL-007 — unset requires a key")]
    public void Unset_Requires_Key()
    {
        Assert.Equal(OtelVerbKind.Usage, OtelVerb.Parse("unset").Kind);
        Assert.Equal(OtelVerbKind.Unset, OtelVerb.Parse("unset team").Kind);
    }

    [Fact(DisplayName = "BR-SKILL-007 — extend without topic still parses (Topic null)")]
    public void Extend_Bare()
    {
        var v = OtelVerb.Parse("extend");
        Assert.Equal(OtelVerbKind.Extend, v.Kind);
        Assert.Null(v.Topic);
    }

    [Fact(DisplayName = "BR-SKILL-007 — extend captures the topic")]
    public void Extend_With_Topic()
    {
        var v = OtelVerb.Parse("extend fix the foo");
        Assert.Equal(OtelVerbKind.Extend, v.Kind);
        Assert.Equal("fix the foo", v.Topic);
    }

    [Fact(DisplayName = "BR-SKILL-007 — unknown verb is Usage")]
    public void Unknown_Is_Usage() =>
        Assert.Equal(OtelVerbKind.Usage, OtelVerb.Parse("frobnicate").Kind);
}
