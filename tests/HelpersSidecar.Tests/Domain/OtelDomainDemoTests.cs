using HelpersSidecar.Domain;
using HelpersSidecar.Infrastructure;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-EXTEND-010 — OtelDomainDemo's 14-step skill chain. Per
/// BR-PROCESS-007 the test mocks ISkillDispatchClient at the seam;
/// downstream skills (/otel, /enrich, /weather) have their own
/// domain tests and are not exercised here.
/// </summary>
public class OtelDomainDemoTests
{
    [Fact(DisplayName = "BR-EXTEND-010 — OtelDomainDemo.DomainName is 'otel'")]
    public void DomainName_Is_Otel() =>
        Assert.Equal("otel", new OtelDomainDemo().DomainName);

    [Fact(DisplayName = "BR-EXTEND-010 — RunAsync produces 14 steps with monotonically numbered ids 1..14")]
    public async Task RunAsync_Produces_14_Numbered_Steps()
    {
        var skills = new RecordingSkillDispatchClient();
        var ctx = new DemoContext("test-session", skills);

        var steps = await new OtelDomainDemo().RunAsync(ctx);

        Assert.Equal(14, steps.Count);
        Assert.Equal(Enumerable.Range(1, 14), steps.Select(s => s.Number));
    }

    [Fact(DisplayName = "BR-EXTEND-010 / BR-DEMO-002 — OtelDomainDemo only chains via skills (12 calls — observation steps don't count)")]
    public async Task RunAsync_Chains_Only_Via_Skills()
    {
        var skills = new RecordingSkillDispatchClient();
        var ctx = new DemoContext("test-session", skills);

        await new OtelDomainDemo().RunAsync(ctx);

        // Expected chain: 1 otel-up + 3 otel-set + 1 otel-get + 2 enrich
        // + 4 weather + 1 otel-down = 12. Steps 9 and 13 are JSONL reads
        // (observation, not action) and do NOT count as skill calls.
        Assert.Equal(12, skills.Calls.Count);
        var bySkill = skills.Calls.GroupBy(c => c.SkillName)
                                  .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, bySkill["otel"]);
        Assert.Equal(2, bySkill["enrich"]);
        Assert.Equal(4, bySkill["weather"]);
    }

    [Fact(DisplayName = "BR-EXTEND-010 — /otel up bookends the start; /otel down bookends the end")]
    public async Task Lifecycle_Bookends_Are_First_And_Last_Skill_Calls()
    {
        var skills = new RecordingSkillDispatchClient();
        var ctx = new DemoContext("test-session", skills);

        await new OtelDomainDemo().RunAsync(ctx);

        Assert.Equal(("otel", "up"),
            (skills.Calls.First().SkillName, skills.Calls.First().Args));
        Assert.Equal(("otel", "down"),
            (skills.Calls.Last().SkillName, skills.Calls.Last().Args));
    }

    [Fact(DisplayName = "BR-DEMO-004 — every step has StartedAt < EndedAt and timestamps within the test's wall-clock window")]
    public async Task Steps_Carry_Monotonic_Timestamps()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var skills = new RecordingSkillDispatchClient();
        var ctx = new DemoContext("test-session", skills);

        var steps = await new OtelDomainDemo().RunAsync(ctx);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        foreach (var s in steps)
        {
            Assert.True(s.StartedAt >= before, $"Step {s.Number} StartedAt {s.StartedAt} before test window {before}");
            Assert.True(s.EndedAt <= after, $"Step {s.Number} EndedAt {s.EndedAt} after test window {after}");
            Assert.True(s.StartedAt <= s.EndedAt, $"Step {s.Number} timestamps not monotonic: {s.StartedAt} > {s.EndedAt}");
        }

        // Steps' StartedAt timestamps should be in non-decreasing
        // numerical order (steps run sequentially).
        for (var i = 1; i < steps.Count; i++)
            Assert.True(steps[i].StartedAt >= steps[i - 1].StartedAt,
                $"step {steps[i].Number} StartedAt before step {steps[i - 1].Number}");
    }

    private sealed class RecordingSkillDispatchClient : ISkillDispatchClient
    {
        public List<(string SkillName, string Args)> Calls { get; } = new();

        public Task<SkillDispatchResult> DispatchAsync(string skillName, IReadOnlyDictionary<string, string> form, CancellationToken ct = default)
        {
            var args = form.TryGetValue("args", out var a) ? a : string.Empty;
            Calls.Add((skillName, args));
            // Canned responses keep the demo's PASS-checks happy.
            var body = (skillName, args) switch
            {
                ("otel", "up")        => "collector started: spawned collector as PID 4242",
                ("otel", "down")      => "collector stopped",
                ("otel", "get user")  => "user=Jaie",
                _                     => "ok",
            };
            return Task.FromResult(new SkillDispatchResult(200, body));
        }
    }
}
