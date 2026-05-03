namespace HelpersSidecar.Domain;

/// <summary>
/// How a domain names its plan files. The pattern uses the
/// placeholder <c>{n}</c> for the plan number and <c>{slug}</c>
/// for the topic slug (lower-cased, dashes, ≤64 chars per
/// BR-EXTEND-005). Example for OTEL:
/// <c>"The-OTEL-Plan-{n}-{slug}.md"</c>.
///
/// <see cref="NumberFloor"/> is the lowest plan number this
/// domain considers valid. v1 always uses 1; future domains
/// could continue numbering from a higher floor if they branch
/// from another domain's history.
/// </summary>
public sealed record PlanFileConventions(string Pattern, int NumberFloor = 1)
{
    public string FileNameFor(int number, string slug) =>
        Pattern.Replace("{n}", number.ToString())
               .Replace("{slug}", slug);
}
