using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HelpersSidecar.Tests.SkillConventions;

/// <summary>
/// BR-SECURITY-005 — tracked repo content has no personal
/// identifiers or secret-shaped tokens. Walks `git ls-files`,
/// reads each scannable file, asserts that no pattern from the
/// deny-list appears outside the named exempt files (which are
/// the rule-defining files themselves).
/// </summary>
public class PersonalDataLintTests
{
    private static readonly string ProjectRoot = LocateProjectRoot();

    /// <summary>
    /// Files that legitimately reference forbidden patterns as
    /// data (not as personal info). These describe the rule;
    /// they don't carry leaks. Adding a file to this list
    /// requires a justification line in the BR-SECURITY-005
    /// section of <c>docs/business-rules.md</c>.
    /// </summary>
    private static readonly HashSet<string> ExemptFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/HelpersSidecar/Domain/EnrichmentValue.cs",
        "tests/HelpersSidecar.Tests/Domain/EnrichmentValueTests.cs",
        "tests/HelpersSidecar.Tests/SkillConventions/PersonalDataLintTests.cs",
        "docs/business-rules.md",
        "docs/otel/plans/The-OTEL-Plan.md",
    };

    /// <summary>
    /// File extensions we scan as text. Binary formats are skipped
    /// (we'd produce gibberish matches) and non-tracked extensions
    /// like .pdb are filtered upstream by the `git ls-files` source.
    /// </summary>
    private static readonly HashSet<string> ScannableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".cs", ".go", ".yaml", ".yml", ".json",
        ".sh", ".ps1", ".bat", ".csproj", ".slnx", ".config",
        ".xml", ".html", ".js", ".ts", ".tsx", ".jsx", ".props",
        ".gitignore", ".gitattributes", ".editorconfig",
    };

    private static readonly Regex EmailRegex = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled);

    private static readonly Regex HomePathRegex = new(
        @"(?:C:\\Users\\[A-Za-z0-9._\-]+\\|/home/[a-z0-9_\-]+/|/Users/[A-Za-z0-9._\-]+/)",
        RegexOptions.Compiled);

    private static readonly Regex TokenRegex = new(
        @"\b(?:AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{36,}|gho_[A-Za-z0-9]{36,}|ghu_[A-Za-z0-9]{36,}|ghs_[A-Za-z0-9]{36,}|ghr_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{80,}|sk-[A-Za-z0-9]{40,}|xox[bpars]-[A-Za-z0-9\-]+)\b",
        RegexOptions.Compiled);

    [Fact(DisplayName = "BR-SECURITY-005 — tracked content has no personal identifiers or secret-shaped tokens")]
    public void Tracked_Content_Has_No_Personal_Or_Secret_Patterns()
    {
        var trackedFiles = ListTrackedFiles();
        Assert.NotEmpty(trackedFiles);

        var failures = new List<string>();

        foreach (var rel in trackedFiles)
        {
            if (ExemptFiles.Contains(rel)) continue;

            var ext = Path.GetExtension(rel);
            if (!ScannableExtensions.Contains(ext)) continue;

            var path = Path.Combine(ProjectRoot, rel);
            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch
            {
                // Tolerate transient read errors (file deleted between
                // ls-files and read). Treat as no-violation; the next
                // run picks up any new content.
                continue;
            }

            ScanAndReport(failures, rel, content, EmailRegex, "email");
            ScanAndReport(failures, rel, content, HomePathRegex, "home-path");
            ScanAndReport(failures, rel, content, TokenRegex, "token-shape");
        }

        Assert.True(failures.Count == 0,
            "BR-SECURITY-005 violations:\n  - " + string.Join("\n  - ", failures));
    }

    [Fact(DisplayName = "BR-SECURITY-005 — patterns themselves match expected examples (regex sanity)")]
    public void Regexes_Match_Their_Documented_Shapes()
    {
        Assert.Matches(EmailRegex, "someone@example.com");
        Assert.Matches(EmailRegex, "first.last+tag@sub.domain.org");
        Assert.DoesNotMatch(EmailRegex, "no-at-sign-here");

        Assert.Matches(HomePathRegex, @"C:\Users\Alice\Documents");
        Assert.Matches(HomePathRegex, "/home/bob/code");
        Assert.Matches(HomePathRegex, "/Users/charlie/dev");
        Assert.DoesNotMatch(HomePathRegex, "/usr/local/bin");

        Assert.Matches(TokenRegex, "AKIAIOSFODNN7EXAMPLE");
        Assert.Matches(TokenRegex, "ghp_" + new string('a', 36));
        Assert.Matches(TokenRegex, "sk-" + new string('a', 48));
        Assert.Matches(TokenRegex, "xoxb-1234-abcd");
        Assert.DoesNotMatch(TokenRegex, "AKIA-too-short");
        Assert.DoesNotMatch(TokenRegex, "ghp_short");
    }

    private static void ScanAndReport(List<string> failures, string rel, string content, Regex re, string label)
    {
        foreach (Match m in re.Matches(content))
        {
            var line = LineOf(content, m.Index);
            failures.Add($"{rel}:{line}: {label} '{Truncate(m.Value, 80)}' (BR-SECURITY-005)");
        }
    }

    private static int LineOf(string content, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static IReadOnlyList<string> ListTrackedFiles()
    {
        var psi = new ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = ProjectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace('\\', '/').Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static string LocateProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".claude", "skills"))
                && File.Exists(Path.Combine(dir, "OTEL.slnx")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir || parent is null) break;
            dir = parent;
        }
        throw new DirectoryNotFoundException("could not locate project root from " + AppContext.BaseDirectory);
    }
}
