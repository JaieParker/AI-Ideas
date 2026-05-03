using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Infrastructure;

/// <summary>
/// BR-SKILL-015 — skills own integrity of their dependent surfaces.
/// SkillRewriter is the mechanism. These unit tests cover the v1
/// scope: (a) sidecar-base-URL replacement; (b) sidecar-mode
/// switch (add/remove docker patterns on the lifecycle skill).
/// </summary>
public class SkillRewriterTests
{
    private static string Frontmatter(string name, string allowedTools, bool disable = false) =>
        $"""
        ---
        name: {name}
        description: test fixture skill body, ≥ 50 chars long for description completeness
        argument-hint: [args]
        disable-model-invocation: {(disable ? "true" : "false")}
        allowed-tools: {allowedTools}
        ---

        !`curl http://127.0.0.1:5050/skills/{name}/dispatch -sS --data-urlencode 'args=$ARGUMENTS' || printf 'PRECONDITION_FAIL: ...\n'`

        Body referencing BR-SKILL-015 with a SCHEMA v1 marker.
        """;

    // ---------------- (a) sidecar base URL ----------------

    [Fact(DisplayName = "BR-SKILL-015 — SidecarBaseUrl rewrite replaces literal URL in allowed-tools and ! line")]
    public void SidecarBaseUrl_Replaces_Both_Surfaces()
    {
        var content = Frontmatter("foo",
            "Bash(curl http://127.0.0.1:5050/skills/foo/dispatch *) Skill(otel up *)");
        var (rewritten, changes) = SkillRewriter.ApplySpec(content,
            new RewriteSpec.SidecarBaseUrl("http://127.0.0.1:5050", "http://127.0.0.1:6060"));

        Assert.Single(changes);
        Assert.Contains("http://127.0.0.1:6060/skills/foo/dispatch", rewritten);
        Assert.DoesNotContain("http://127.0.0.1:5050", rewritten);
    }

    [Fact(DisplayName = "BR-SKILL-015 — SidecarBaseUrl rewrite is no-op when old == new")]
    public void SidecarBaseUrl_Noop_When_Unchanged()
    {
        var content = Frontmatter("foo",
            "Bash(curl http://127.0.0.1:5050/skills/foo/dispatch *)");
        var (rewritten, changes) = SkillRewriter.ApplySpec(content,
            new RewriteSpec.SidecarBaseUrl("http://127.0.0.1:5050", "http://127.0.0.1:5050"));

        Assert.Empty(changes);
        Assert.Equal(content, rewritten);
    }

    // ---------------- (b) sidecar mode ----------------

    [Fact(DisplayName = "BR-SKILL-015 — SetSidecarMode container adds docker patterns to lifecycle skill only")]
    public void SetSidecarMode_Container_Adds_Docker_Patterns()
    {
        var content = Frontmatter("skill-bootstrap",
            "Bash(dotnet --version) Bash(dotnet build src/HelpersSidecar/HelpersSidecar.csproj *)");
        var spec = new RewriteSpec.SetSidecarMode(
            TargetMode: SidecarMode.Container,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: "claude-helpers-sidecar:dev",
            HostPort: 5050,
            ContainerNamePrefix: "claude-helpers-sidecar-main");
        var (rewritten, changes) = SkillRewriter.ApplySpec(content, spec);

        Assert.Single(changes);
        Assert.Contains("Bash(docker run -p 127.0.0.1:5050:5050 claude-helpers-sidecar:dev *)", rewritten);
        Assert.Contains("Bash(docker stop claude-helpers-sidecar-main-* *)", rewritten);
        Assert.Contains("Bash(docker ps *)", rewritten);
        Assert.Contains("Bash(docker build -t claude-helpers-sidecar:dev src/HelpersSidecar/*)", rewritten);
    }

    [Fact(DisplayName = "BR-SKILL-015 — SetSidecarMode direct removes docker patterns")]
    public void SetSidecarMode_Direct_Removes_Docker_Patterns()
    {
        var startingTools =
            "Bash(dotnet --version) " +
            "Bash(docker run -p 127.0.0.1:5050:5050 claude-helpers-sidecar:dev *) " +
            "Bash(docker stop claude-helpers-sidecar-main-* *) " +
            "Bash(docker ps *) " +
            "Bash(docker build -t claude-helpers-sidecar:dev src/HelpersSidecar/*)";
        var content = Frontmatter("skill-bootstrap", startingTools);
        var spec = new RewriteSpec.SetSidecarMode(
            TargetMode: SidecarMode.Direct,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: "claude-helpers-sidecar:dev",
            HostPort: 5050,
            ContainerNamePrefix: "claude-helpers-sidecar-main");
        var (rewritten, changes) = SkillRewriter.ApplySpec(content, spec);

        Assert.Single(changes);
        Assert.DoesNotContain("docker run", rewritten);
        Assert.DoesNotContain("docker stop", rewritten);
        Assert.DoesNotContain("docker ps", rewritten);
        Assert.DoesNotContain("docker build", rewritten);
        Assert.Contains("Bash(dotnet --version)", rewritten);
    }

    [Fact(DisplayName = "BR-SKILL-015 — SetSidecarMode skips non-target skills")]
    public void SetSidecarMode_NonTargetSkill_Is_NoOp()
    {
        var content = Frontmatter("otel",
            "Bash(curl http://127.0.0.1:5050/skills/otel/dispatch *) Skill(extend-skills *)");
        var spec = new RewriteSpec.SetSidecarMode(
            TargetMode: SidecarMode.Container,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: "claude-helpers-sidecar:dev",
            HostPort: 5050,
            ContainerNamePrefix: "claude-helpers-sidecar-main");
        var (rewritten, changes) = SkillRewriter.ApplySpec(content, spec);

        Assert.Empty(changes);
        Assert.Equal(content, rewritten);
    }

    [Fact(DisplayName = "BR-SKILL-015 — SetSidecarMode is idempotent (already-container → container is no-op)")]
    public void SetSidecarMode_Idempotent()
    {
        var startingTools =
            "Bash(dotnet --version) " +
            "Bash(docker run -p 127.0.0.1:5050:5050 claude-helpers-sidecar:dev *) " +
            "Bash(docker stop claude-helpers-sidecar-main-* *) " +
            "Bash(docker ps *) " +
            "Bash(docker build -t claude-helpers-sidecar:dev src/HelpersSidecar/*)";
        var content = Frontmatter("skill-bootstrap", startingTools);
        var spec = new RewriteSpec.SetSidecarMode(
            TargetMode: SidecarMode.Container,
            LifecycleSkillName: "skill-bootstrap",
            ContainerImage: "claude-helpers-sidecar:dev",
            HostPort: 5050,
            ContainerNamePrefix: "claude-helpers-sidecar-main");
        var (rewritten, changes) = SkillRewriter.ApplySpec(content, spec);

        Assert.Empty(changes);
        Assert.Equal(content, rewritten);
    }

    // ---------------- BR-SKILL-004 alignment ----------------

    [Fact(DisplayName = "BR-SKILL-015 — Doctor refuses to walk a non-existent skills root")]
    public void Doctor_Refuses_Missing_Root()
    {
        var rewriter = new SkillRewriter();
        Assert.Throws<DirectoryNotFoundException>(() =>
            rewriter.Doctor("nonexistent-dir-for-test", new RewriteSpec.SidecarBaseUrl("a", "b")));
    }

    // ---------------- (c) Claude Code OTLP endpoint ----------------

    [Fact(DisplayName = "BR-SKILL-015 — ClaudeCodeOtlpEndpoint creates settings.local.json with env block when missing")]
    public void ClaudeCodeOtlpEndpoint_Creates_File_When_Missing()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"plan13-test-{Guid.NewGuid():N}.json");
        try
        {
            var rewriter = new SkillRewriter();
            var report = rewriter.ApplyClaudeCodeOtlpEndpoint(
                tmp, new RewriteSpec.ClaudeCodeOtlpEndpoint("http://127.0.0.1:14318"), write: true);

            Assert.True(report.Drifted);
            Assert.True(File.Exists(tmp));
            var content = File.ReadAllText(tmp);
            Assert.Contains("\"OTEL_EXPORTER_OTLP_ENDPOINT\"", content);
            Assert.Contains("http://127.0.0.1:14318", content);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact(DisplayName = "BR-SKILL-015 — ClaudeCodeOtlpEndpoint preserves unrelated keys")]
    public void ClaudeCodeOtlpEndpoint_Preserves_Other_Keys()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"plan13-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, """
            {
              "permissions": { "allow": ["Bash(ls *)"] },
              "env": { "FOO": "bar" }
            }
            """);
            var rewriter = new SkillRewriter();
            rewriter.ApplyClaudeCodeOtlpEndpoint(tmp,
                new RewriteSpec.ClaudeCodeOtlpEndpoint("http://127.0.0.1:14318"), write: true);

            var content = File.ReadAllText(tmp);
            Assert.Contains("\"FOO\": \"bar\"", content);
            Assert.Contains("\"OTEL_EXPORTER_OTLP_ENDPOINT\": \"http://127.0.0.1:14318\"", content);
            Assert.Contains("\"Bash(ls *)\"", content);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact(DisplayName = "BR-SKILL-015 — ClaudeCodeOtlpEndpoint is no-op when value already matches")]
    public void ClaudeCodeOtlpEndpoint_Noop_When_Match()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"plan13-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, """
            {
              "env": { "OTEL_EXPORTER_OTLP_ENDPOINT": "http://127.0.0.1:14318" }
            }
            """);
            var rewriter = new SkillRewriter();
            var report = rewriter.ApplyClaudeCodeOtlpEndpoint(tmp,
                new RewriteSpec.ClaudeCodeOtlpEndpoint("http://127.0.0.1:14318"), write: true);

            Assert.False(report.Drifted);
            Assert.Empty(report.Changes);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact(DisplayName = "BR-SKILL-015 — ClaudeCodeOtlpEndpoint refuses to clobber malformed JSON")]
    public void ClaudeCodeOtlpEndpoint_Refuses_Malformed_Json()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"plan13-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, "{ this is not json");
            var before = File.ReadAllText(tmp);

            var rewriter = new SkillRewriter();
            var report = rewriter.ApplyClaudeCodeOtlpEndpoint(tmp,
                new RewriteSpec.ClaudeCodeOtlpEndpoint("http://127.0.0.1:14318"), write: true);

            Assert.False(report.Drifted);
            Assert.Single(report.Changes);
            Assert.Contains("refused", report.Changes[0]);
            Assert.Equal(before, File.ReadAllText(tmp)); // file untouched
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
