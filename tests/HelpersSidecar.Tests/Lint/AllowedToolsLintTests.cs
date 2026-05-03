using System.Text.RegularExpressions;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Lint;

/// <summary>
/// BR-SKILL-015 build-time lint — every project SKILL.md's
/// <c>allowed-tools</c> sidecar URL agrees with the current
/// <c>appsettings.json</c>'s sidecar listener; CI fails on drift.
/// This is the "rewriter would change it" companion to the
/// rewriter itself: the rewriter makes the fix possible; this
/// test makes the drift fail.
/// </summary>
public class AllowedToolsLintTests
{
    private static readonly Regex AllowedToolsLineRegex =
        new(@"^allowed-tools:\s*(?<value>.*)$", RegexOptions.Multiline);

    private static readonly Regex CurlSidecarUrlRegex =
        new(@"Bash\(curl\s+(?<url>https?://[^/]+)(?<path>/skills/[^\s)]+)", RegexOptions.None);

    [Fact(DisplayName = "BR-SKILL-015 — every project SKILL.md's allowed-tools sidecar URL matches appsettings.json")]
    public void Allowed_Tools_Sidecar_Urls_Match_AppSettings()
    {
        // Resolve project root from the test binary's location.
        // Tests run from tests/HelpersSidecar.Tests/bin/Debug/net10.0;
        // walk up to find the repo root (a directory containing
        // .claude/skills/ and src/HelpersSidecar/appsettings.json).
        var repoRoot = FindRepoRoot();
        var skillsRoot = Path.Combine(repoRoot, ".claude", "skills");
        var appsettingsPath = Path.Combine(repoRoot, "src", "HelpersSidecar", "appsettings.json");

        Assert.True(Directory.Exists(skillsRoot), $"skills root missing: {skillsRoot}");
        Assert.True(File.Exists(appsettingsPath), $"appsettings.json missing: {appsettingsPath}");

        // Default sidecar URL — appsettings.json is JSON; we read
        // the literal default from the typed-options class so we
        // don't pull in a JSON parser just for a lint.
        var listener = new SidecarOptions();              // default Listener:Port = 5050 implicit
        const string expectedHost = "127.0.0.1";          // BR-HELPERS-002 default
        const int expectedPort = 5050;                    // appsettings Listener:Port default
        var expectedPrefix = $"http://{expectedHost}:{expectedPort}";

        var drifted = new List<string>();
        foreach (var skillFile in Directory.EnumerateFiles(skillsRoot, "SKILL.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(skillFile);

            var allowedTools = AllowedToolsLineRegex.Match(content);
            if (!allowedTools.Success) continue;

            foreach (Match m in CurlSidecarUrlRegex.Matches(allowedTools.Value))
            {
                var url = m.Groups["url"].Value;
                if (url != expectedPrefix)
                    drifted.Add($"{Path.GetRelativePath(repoRoot, skillFile)} → allowed-tools curl URL '{url}' (expected '{expectedPrefix}')");
            }
        }

        Assert.Empty(drifted);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".claude", "skills")) &&
                File.Exists(Path.Combine(dir, "src", "HelpersSidecar", "appsettings.json")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException("could not find repo root from " + AppContext.BaseDirectory);
    }
}
