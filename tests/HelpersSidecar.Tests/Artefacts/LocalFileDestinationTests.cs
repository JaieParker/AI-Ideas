using System.Text;
using HelpersSidecar.Artefacts;

namespace HelpersSidecar.Tests.Artefacts;

/// <summary>
/// BR-PROCESS-015 — LocalFileDestination resolves keys against
/// the project root, creates missing directories, and writes
/// bodies verbatim. Tests use a temp directory so they don't
/// depend on CWD.
/// </summary>
public class LocalFileDestinationTests : IDisposable
{
    private readonly string _tempRoot;

    public LocalFileDestinationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "otel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact(DisplayName = "BR-PROCESS-015 — Name is 'local-fs' and Kind is LocalFile")]
    public async Task Name_And_Kind_Are_Stable()
    {
        var d = new LocalFileDestination(_tempRoot);
        Assert.Equal("local-fs", d.Name);
        Assert.Equal(DestinationKind.LocalFile, d.Kind);
        await Task.CompletedTask;
    }

    [Fact(DisplayName = "BR-PROCESS-015 — relative key resolves under project root")]
    public async Task Relative_Key_Resolves_Under_Root()
    {
        var d = new LocalFileDestination(_tempRoot);
        var body = Encoding.UTF8.GetBytes("hello").AsMemory();

        await d.WriteAsync("output/foo.md", body, MakeMd());

        var path = Path.Combine(_tempRoot, "output", "foo.md");
        Assert.True(File.Exists(path));
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — missing parent directories are created")]
    public async Task Parent_Dirs_Created()
    {
        var d = new LocalFileDestination(_tempRoot);
        var body = Encoding.UTF8.GetBytes("x").AsMemory();

        await d.WriteAsync("a/b/c/d.md", body, MakeMd());

        Assert.True(Directory.Exists(Path.Combine(_tempRoot, "a", "b", "c")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "a", "b", "c", "d.md")));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — re-writing the same key overwrites the body")]
    public async Task Rewrite_Overwrites()
    {
        var d = new LocalFileDestination(_tempRoot);
        await d.WriteAsync("foo.md", Encoding.UTF8.GetBytes("first").AsMemory(), MakeMd());
        await d.WriteAsync("foo.md", Encoding.UTF8.GetBytes("second").AsMemory(), MakeMd());

        var path = Path.Combine(_tempRoot, "foo.md");
        Assert.Equal("second", File.ReadAllText(path));
    }

    [Fact(DisplayName = "BR-PROCESS-015 — absolute key writes outside project root (caller's responsibility)")]
    public async Task Absolute_Key_Writes_Verbatim()
    {
        // Keep the absolute write inside the temp dir for cleanup.
        var absolute = Path.Combine(_tempRoot, "absolute.md");
        var d = new LocalFileDestination(_tempRoot);
        await d.WriteAsync(absolute, Encoding.UTF8.GetBytes("abs").AsMemory(), MakeMd());
        Assert.Equal("abs", File.ReadAllText(absolute));
    }

    private static ArtefactMetadata MakeMd() =>
        new("FOO_REPORT", 1, DateTimeOffset.UtcNow, "Test.Producer", "otel");
}
