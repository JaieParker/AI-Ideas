using System.Text;
using HelpersSidecar.Artefacts;

namespace HelpersSidecar.Tests.Artefacts;

/// <summary>
/// BR-PROCESS-015 — ArtefactWriter renders the key template,
/// walks the spec's destinations, and applies per-destination
/// failure modes (Required vs BestEffort).
/// </summary>
public class ArtefactWriterTests
{
    [Fact(DisplayName = "BR-PROCESS-015 — KeyTemplate <utc-ts> is replaced with current UTC")]
    public async Task KeyTemplate_Renders_UtcTs()
    {
        var registry = OneSpec("foo", "output/foo-<utc-ts>.md", "local-fs", DestinationFailureMode.Required);
        var dest = new RecordingDestination("local-fs");
        var writer = new ArtefactWriter(registry, new[] { (IArtefactDestination)dest });

        var result = await writer.WriteAsync("foo",
            new Dictionary<string, string>(), Encoding.UTF8.GetBytes("body").AsMemory());

        Assert.Single(dest.Writes);
        var key = dest.Writes[0].Key;
        Assert.StartsWith("output/foo-", key);
        Assert.EndsWith(".md", key);
        Assert.DoesNotContain("<utc-ts>", key);
        Assert.True(result.Success);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — caller-supplied template params are substituted")]
    public async Task Caller_Params_Substituted()
    {
        var registry = OneSpec("foo", "output/<scope>/<utc-ts>.md", "local-fs", DestinationFailureMode.Required);
        var dest = new RecordingDestination("local-fs");
        var writer = new ArtefactWriter(registry, new[] { (IArtefactDestination)dest });

        await writer.WriteAsync("foo",
            new Dictionary<string, string> { ["scope"] = "abc" },
            Encoding.UTF8.GetBytes("body").AsMemory());

        Assert.StartsWith("output/abc/", dest.Writes[0].Key);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — UserEdited artefacts cannot be written through IArtefactWriter")]
    public async Task UserEdited_Throws()
    {
        var registry = new ArtefactRegistry(new[]
        {
            MakeSpec("user-edited", "docs/foo.md", ArtefactLifecycle.UserEdited,
                Array.Empty<DestinationRef>()),
        });
        var writer = new ArtefactWriter(registry, Array.Empty<IArtefactDestination>());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await writer.WriteAsync("user-edited",
                new Dictionary<string, string>(),
                ReadOnlyMemory<byte>.Empty));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — Required destination failure surfaces Success=false")]
    public async Task Required_Destination_Failure_Surfaces()
    {
        var registry = OneSpec("foo", "f.md", "broken", DestinationFailureMode.Required);
        var broken = new ThrowingDestination("broken");
        var writer = new ArtefactWriter(registry, new[] { (IArtefactDestination)broken });

        var result = await writer.WriteAsync("foo",
            new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty);

        Assert.False(result.Success);
        Assert.False(result.DestinationResults[0].Success);
        Assert.Equal("broken", result.DestinationResults[0].DestinationName);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — BestEffort destination failure does NOT fail the write")]
    public async Task BestEffort_Failure_Does_Not_Fail_Write()
    {
        var registry = new ArtefactRegistry(new[]
        {
            MakeSpec("foo", "f.md", ArtefactLifecycle.OneShot, new[]
            {
                new DestinationRef("good", DestinationFailureMode.Required),
                new DestinationRef("flaky", DestinationFailureMode.BestEffort),
            }),
        });
        var good = new RecordingDestination("good");
        var flaky = new ThrowingDestination("flaky");
        var writer = new ArtefactWriter(registry, new IArtefactDestination[] { good, flaky });

        var result = await writer.WriteAsync("foo",
            new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty);

        Assert.True(result.Success);
        Assert.True(result.DestinationResults.Single(r => r.DestinationName == "good").Success);
        Assert.False(result.DestinationResults.Single(r => r.DestinationName == "flaky").Success);
    }

    [Fact(DisplayName = "BR-PROCESS-015 — unregistered destination name surfaces as failure")]
    public async Task Unregistered_Destination_Surfaces_As_Failure()
    {
        var registry = OneSpec("foo", "f.md", "missing", DestinationFailureMode.Required);
        var writer = new ArtefactWriter(registry, Array.Empty<IArtefactDestination>());

        var result = await writer.WriteAsync("foo",
            new Dictionary<string, string>(), ReadOnlyMemory<byte>.Empty);

        Assert.False(result.Success);
        Assert.Contains("not registered", result.DestinationResults[0].Error);
    }

    // -------- helpers --------

    private static ArtefactRegistry OneSpec(string name, string keyTemplate, string destName, DestinationFailureMode mode) =>
        new(new[] { MakeSpec(name, keyTemplate, ArtefactLifecycle.OneShot,
            new[] { new DestinationRef(destName, mode) }) });

    private static ArtefactSpec MakeSpec(string name, string keyTemplate,
        ArtefactLifecycle lifecycle, IReadOnlyList<DestinationRef> destinations) =>
        new(
            Name: name, KeyTemplate: keyTemplate,
            Destinations: destinations,
            SchemaName: "FOO", SchemaVersion: 1,
            Lifecycle: lifecycle,
            GitTracked: false,
            Producer: "Test.Producer",
            GoverningBR: "BR-FOO-001",
            Owner: "otel",
            CostClass: ArtefactCostClass.Free);

    private sealed class RecordingDestination : IArtefactDestination
    {
        public string Name { get; }
        public DestinationKind Kind => DestinationKind.LocalFile;
        public List<(string Key, ArtefactMetadata Md)> Writes { get; } = new();

        public RecordingDestination(string name) { Name = name; }

        public Task WriteAsync(string key, ReadOnlyMemory<byte> body, ArtefactMetadata md, CancellationToken ct = default)
        {
            Writes.Add((key, md));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDestination : IArtefactDestination
    {
        public string Name { get; }
        public DestinationKind Kind => DestinationKind.LocalFile;
        public ThrowingDestination(string name) { Name = name; }
        public Task WriteAsync(string key, ReadOnlyMemory<byte> body, ArtefactMetadata md, CancellationToken ct = default)
            => throw new InvalidOperationException("destination intentionally broken for test");
    }
}
