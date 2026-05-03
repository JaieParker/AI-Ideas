using HelpersSidecar.Artefacts;

namespace HelpersSidecar.Tests.Artefacts;

/// <summary>
/// BR-PROCESS-015 — registry collects every registered ArtefactSpec
/// with name-based + filtered queries. Mirrors DomainResolver shape.
/// </summary>
public class ArtefactRegistryTests
{
    private static ArtefactSpec MakeSpec(string name, string? owner = "otel",
        ArtefactLifecycle lifecycle = ArtefactLifecycle.OneShot,
        string schemaName = "FOO_REPORT") =>
        new(
            Name: name,
            KeyTemplate: $"output/{name}-<utc-ts>.md",
            Destinations: new[] { new DestinationRef("local-fs", DestinationFailureMode.Required) },
            SchemaName: schemaName, SchemaVersion: 1,
            Lifecycle: lifecycle,
            GitTracked: false,
            Producer: "Some.Producer",
            GoverningBR: "BR-FOO-001",
            Owner: owner,
            CostClass: ArtefactCostClass.Free);

    [Fact(DisplayName = "BR-PROCESS-015 — Get(name) returns the registered spec")]
    public void Get_Returns_Registered_Spec()
    {
        var registry = new ArtefactRegistry(new[] { MakeSpec("alpha"), MakeSpec("beta") });
        Assert.Equal("alpha", registry.Get("alpha").Name);
        Assert.Equal("beta", registry.Get("beta").Name);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — Get(unknown) throws KeyNotFoundException")]
    public void Get_Unknown_Throws()
    {
        var registry = new ArtefactRegistry(new[] { MakeSpec("alpha") });
        Assert.Throws<KeyNotFoundException>(() => registry.Get("missing"));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — TryGet returns false on unknown name")]
    public void TryGet_Returns_False_On_Unknown()
    {
        var registry = new ArtefactRegistry(new[] { MakeSpec("alpha") });
        Assert.False(registry.TryGet("missing", out var spec));
        Assert.Null(spec);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — duplicate Name throws InvalidOperationException")]
    public void Duplicate_Name_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ArtefactRegistry(new[] { MakeSpec("alpha"), MakeSpec("alpha") }));
        Assert.Contains("duplicate ArtefactSpec.Name", ex.Message);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — ByOwner filters correctly including null owner")]
    public void ByOwner_Filters_Including_Null()
    {
        var registry = new ArtefactRegistry(new[]
        {
            MakeSpec("a", owner: "otel"),
            MakeSpec("b", owner: "cross-domain"),
            MakeSpec("c", owner: null),
            MakeSpec("d", owner: "otel"),
        });

        Assert.Equal(2, registry.ByOwner("otel").Count);
        Assert.Single(registry.ByOwner("cross-domain"));
        Assert.Single(registry.ByOwner(null));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — ByLifecycle filters correctly")]
    public void ByLifecycle_Filters()
    {
        var registry = new ArtefactRegistry(new[]
        {
            MakeSpec("a", lifecycle: ArtefactLifecycle.OneShot),
            MakeSpec("b", lifecycle: ArtefactLifecycle.UserEdited),
            MakeSpec("c", lifecycle: ArtefactLifecycle.RuntimeState),
            MakeSpec("d", lifecycle: ArtefactLifecycle.OneShot),
        });

        Assert.Equal(2, registry.ByLifecycle(ArtefactLifecycle.OneShot).Count);
        Assert.Single(registry.ByLifecycle(ArtefactLifecycle.UserEdited));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — BySchema filters correctly (case-sensitive)")]
    public void BySchema_Filters()
    {
        var registry = new ArtefactRegistry(new[]
        {
            MakeSpec("a", schemaName: "DEMO_REPORT"),
            MakeSpec("b", schemaName: "demo_report"),  // different case → not the same
            MakeSpec("c", schemaName: "AI_LEVEL_REPORT"),
        });

        Assert.Single(registry.BySchema("DEMO_REPORT"));
        Assert.Single(registry.BySchema("demo_report"));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — ArtefactSpecs.All registers ≥ 10 known artefacts")]
    public void ArtefactSpecs_All_Has_Expected_Entries()
    {
        // Spot-check the well-known ones; full biconditional in BiconditionalTests.
        var registry = new ArtefactRegistry(ArtefactSpecs.All);
        Assert.True(registry.TryGet("demo-report", out _));
        Assert.True(registry.TryGet("ai-level-report", out _));
        Assert.True(registry.TryGet("plans-index", out _));
        Assert.True(registry.TryGet("sidecar-pid", out _));
        Assert.True(registry.TryGet("retros", out _));
        Assert.True(ArtefactSpecs.All.Count >= 10);
    }
}
