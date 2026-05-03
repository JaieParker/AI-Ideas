using HelpersSidecar.Application;

namespace HelpersSidecar.Tests.Application;

/// <summary>
/// BR-SKILL-013 — SkillFileParser tolerates real-world SKILL.md
/// shapes: well-formed frontmatter, no frontmatter, malformed
/// frontmatter, mixed line endings.
/// </summary>
public class SkillFileParserTests
{
    [Fact(DisplayName = "BR-SKILL-013 — well-formed frontmatter parses every key")]
    public void Parses_Well_Formed_Frontmatter()
    {
        var text = "---\nname: foo\ndescription: a skill\nargument-hint: <bar>\nallowed-tools: Bash(curl http://x *)\n---\n# body\nstuff\n";
        var sf = SkillFileParser.Parse("foo", text);

        Assert.Equal("foo", sf.Frontmatter["name"]);
        Assert.Equal("a skill", sf.Frontmatter["description"]);
        Assert.Equal("<bar>", sf.Frontmatter["argument-hint"]);
        Assert.Equal("Bash(curl http://x *)", sf.Frontmatter["allowed-tools"]);
        Assert.Contains("# body", sf.Body);
    }

    [Fact(DisplayName = "BR-SKILL-013 — empty content yields empty frontmatter and body")]
    public void Empty_Content_Yields_Empty_Result()
    {
        var sf = SkillFileParser.Parse("foo", string.Empty);
        Assert.Empty(sf.Frontmatter);
        Assert.Empty(sf.Body);
    }

    [Fact(DisplayName = "BR-SKILL-013 — file with no frontmatter has empty frontmatter, full body")]
    public void No_Frontmatter_Means_Body_Only()
    {
        var text = "# just a body\nno frontmatter at all\n";
        var sf = SkillFileParser.Parse("foo", text);
        Assert.Empty(sf.Frontmatter);
        Assert.Contains("just a body", sf.Body);
    }

    [Fact(DisplayName = "BR-SKILL-013 — opening '---' but no closing falls back to body-only")]
    public void Unterminated_Frontmatter_Treated_As_Body()
    {
        var text = "---\nname: foo\nbut no closing marker\n# body\n";
        var sf = SkillFileParser.Parse("foo", text);
        Assert.Empty(sf.Frontmatter);
        Assert.Contains("# body", sf.Body);
    }

    [Fact(DisplayName = "BR-SKILL-013 — Windows line endings parse the same as Unix")]
    public void Windows_Line_Endings_Parse_The_Same()
    {
        var text = "---\r\nname: foo\r\ndescription: a skill\r\n---\r\n# body\r\n";
        var sf = SkillFileParser.Parse("foo", text);
        Assert.Equal("foo", sf.Frontmatter["name"]);
        Assert.Equal("a skill", sf.Frontmatter["description"]);
        Assert.Contains("# body", sf.Body);
    }

    [Fact(DisplayName = "BR-SKILL-013 — quoted values have surrounding double-quotes stripped")]
    public void Quoted_Values_Have_Quotes_Stripped()
    {
        var text = "---\nname: \"foo\"\ndescription: \"with quotes\"\n---\n";
        var sf = SkillFileParser.Parse("foo", text);
        Assert.Equal("foo", sf.Frontmatter["name"]);
        Assert.Equal("with quotes", sf.Frontmatter["description"]);
    }

    [Fact(DisplayName = "BR-SKILL-013 — colons in values are preserved (only first colon is the delimiter)")]
    public void Colons_In_Values_Preserved()
    {
        var text = "---\nallowed-tools: Bash(curl http://127.0.0.1:5050 *)\n---\n";
        var sf = SkillFileParser.Parse("foo", text);
        Assert.Equal("Bash(curl http://127.0.0.1:5050 *)", sf.Frontmatter["allowed-tools"]);
    }
}
