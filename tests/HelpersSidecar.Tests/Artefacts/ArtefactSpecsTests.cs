using HelpersSidecar.Artefacts;

namespace HelpersSidecar.Tests.Artefacts;

/// <summary>
/// BR-PROCESS-015's biconditional: every registered spec's
/// Producer type resolves (for programmatic specs); every
/// schema named in BR-PROCESS-013's catalogue has at least one
/// registry entry.
/// </summary>
public class ArtefactSpecsTests
{
    private static readonly ArtefactRegistry Registry = new(ArtefactSpecs.All);

    [Fact(DisplayName = "BR-PROCESS-015 — every programmatic Producer type resolves to a real type")]
    public void Every_Programmatic_Producer_Resolves()
    {
        foreach (var spec in Registry.All)
        {
            if (spec.Lifecycle == ArtefactLifecycle.UserEdited) continue;
            // Producer field carries one of two shapes:
            //  (a) a fully-qualified type name (e.g. "HelpersSidecar.Domain.X") —
            //      Type.GetType MUST resolve it, OR
            //  (b) a human-readable note containing whitespace or parens
            //      (e.g. "(collector — file-exporter)", "HelpersSidecar.Program
            //      (lifetime hooks)") — registered for visibility only;
            //      skipped here.
            if (spec.Producer.Contains(' ') || spec.Producer.Contains('('))
                continue;

            var type = Type.GetType(spec.Producer + ", HelpersSidecar");
            Assert.NotNull(type);
        }
    }

    [Fact(DisplayName = "BR-PROCESS-015 — every spec has a non-empty Name and KeyTemplate")]
    public void Every_Spec_Has_Name_And_Key_Template()
    {
        foreach (var spec in Registry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.Name), $"empty Name on spec");
            Assert.False(string.IsNullOrWhiteSpace(spec.KeyTemplate),
                $"empty KeyTemplate on spec '{spec.Name}'");
        }
    }

    [Fact(DisplayName = "BR-PROCESS-015 — Names are unique across the catalogue")]
    public void Names_Are_Unique()
    {
        var names = Registry.All.Select(s => s.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory(DisplayName = "BR-PROCESS-013 — catalogue schemas have a registry entry")]
    [InlineData("DEMO_REPORT")]
    [InlineData("AI_LEVEL_REPORT")]
    [InlineData("PLAN_INDEX")]
    public void Catalogue_Schemas_Are_Registered(string schemaName)
    {
        var matches = Registry.BySchema(schemaName);
        Assert.NotEmpty(matches);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — UserEdited specs have empty Destinations list")]
    public void UserEdited_Specs_Have_No_Destinations()
    {
        foreach (var spec in Registry.All.Where(s => s.Lifecycle == ArtefactLifecycle.UserEdited))
        {
            Assert.Empty(spec.Destinations);
        }
    }

    [Fact(DisplayName = "BR-PROCESS-015 — programmatic specs have at least one Destination")]
    public void Programmatic_Specs_Have_Destinations()
    {
        foreach (var spec in Registry.All.Where(s => s.Lifecycle != ArtefactLifecycle.UserEdited))
        {
            Assert.NotEmpty(spec.Destinations);
        }
    }

    [Fact(DisplayName = "BR-SECURITY-004 — no remote destinations registered in v1")]
    public void No_Remote_Destinations_Yet()
    {
        // Plan-11 ships only "local-fs". A future plan that adds e.g.
        // "s3:audit-bucket" must update this test along with shipping
        // the matching IArtefactDestination impl AND the BR-SECURITY-004
        // opt-in machinery.
        foreach (var spec in Registry.All)
        {
            foreach (var dest in spec.Destinations)
            {
                Assert.Equal("local-fs", dest.DestinationName);
            }
        }
    }
}
