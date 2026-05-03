namespace HelpersSidecar.Application;

/// <summary>
/// Parses a SKILL.md file into typed parts. Frontmatter is the
/// YAML-shaped block delimited by <c>---</c> markers at the top
/// of the file; the body is everything after.
///
/// <para>Tolerant by design: missing frontmatter, malformed
/// keys, and inconsistent line endings all return a populated
/// <see cref="SkillFile"/> with whatever could be parsed —
/// <see cref="AiLevelChecker"/> reports the absences as score
/// hits, not as parse errors.</para>
/// </summary>
public static class SkillFileParser
{
    public static SkillFile Parse(string skillName, string fullText)
    {
        if (string.IsNullOrEmpty(fullText))
            return new SkillFile(skillName, new Dictionary<string, string>(StringComparer.Ordinal), Body: string.Empty);

        var normalised = fullText.Replace("\r\n", "\n");
        var lines = normalised.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            // No frontmatter at all → entire content is body.
            return new SkillFile(skillName, new Dictionary<string, string>(StringComparer.Ordinal), Body: normalised);
        }

        var endIdx = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIdx = i;
                break;
            }
        }

        if (endIdx < 0)
        {
            // Opening --- but no closing → treat as no frontmatter.
            return new SkillFile(skillName, new Dictionary<string, string>(StringComparer.Ordinal), Body: normalised);
        }

        var frontmatter = ParseFrontmatter(lines, 1, endIdx);
        var body = string.Join('\n', lines.Skip(endIdx + 1));

        return new SkillFile(skillName, frontmatter, body);
    }

    private static IReadOnlyDictionary<string, string> ParseFrontmatter(string[] lines, int start, int end)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = start; i < end; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            // Strip surrounding quotes if present.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            if (!string.IsNullOrEmpty(key))
                dict[key] = value;
        }
        return dict;
    }
}

public sealed record SkillFile(
    string Name,
    IReadOnlyDictionary<string, string> Frontmatter,
    string Body);
