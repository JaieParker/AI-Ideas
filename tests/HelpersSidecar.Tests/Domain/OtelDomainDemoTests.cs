using HelpersSidecar.Domain;

namespace HelpersSidecar.Tests.Domain;

/// <summary>
/// BR-EXTEND-010 (amended Plan-23) — OtelDomainDemo registers the
/// OTEL domain's IDemoTarget. Plan-23 retired the in-process
/// loopback chain (BR-DEMO-002 amended); the demo now returns a
/// data-only plan that the SKILL.md body executes via the Claude
/// Code Skill tool. Tests assert the plan's shape, not execution.
/// </summary>
public class OtelDomainDemoTests
{
    [Fact(DisplayName = "BR-EXTEND-010 — OtelDomainDemo.TargetName is 'otel' and TargetKind is 'domain'")]
    public void Target_Identity_Is_Otel_Domain()
    {
        var d = new OtelDomainDemo();
        Assert.Equal("otel", d.TargetName);
        Assert.Equal("domain", d.TargetKind);
    }

    [Fact(DisplayName = "BR-EXTEND-010 — OtelDomainDemo registers exactly one IsDefault=true case named 'happy-path'")]
    public void Default_Case_Is_HappyPath()
    {
        var d = new OtelDomainDemo();
        var defaults = d.Demos.Where(c => c.IsDefault).ToArray();
        Assert.Single(defaults);
        Assert.Equal("happy-path", defaults[0].Name);
    }

    [Fact(DisplayName = "BR-EXTEND-010 — happy-path Plan returns 14 steps with monotonically numbered ids 1..14")]
    public async Task HappyPath_Returns_14_Numbered_Steps()
    {
        var d = new OtelDomainDemo();
        var ctx = new DemoContext("test-session");
        var steps = await d.Demos.First(c => c.IsDefault).Plan(ctx, CancellationToken.None);

        Assert.Equal(14, steps.Count);
        Assert.Equal(Enumerable.Range(1, 14), steps.Select(s => s.Number));
    }

    [Fact(DisplayName = "BR-DEMO-002 (amended) — happy-path declares 12 invoke steps + 2 observe steps; chained skills are otel/enrich/weather only")]
    public async Task HappyPath_Step_Mix_And_Skill_Set()
    {
        var d = new OtelDomainDemo();
        var ctx = new DemoContext("test-session");
        var steps = await d.Demos.First(c => c.IsDefault).Plan(ctx, CancellationToken.None);

        var invokes = steps.Where(s => s.Kind == "invoke").ToArray();
        var observes = steps.Where(s => s.Kind == "observe").ToArray();

        Assert.Equal(12, invokes.Length);
        Assert.Equal(2, observes.Length);

        var bySkill = invokes.GroupBy(s => s.Skill).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, bySkill["otel"]);     // up + 3 sets + get + down
        Assert.Equal(2, bySkill["enrich"]);   // ticket.id JA-0001, JA-0002
        Assert.Equal(4, bySkill["weather"]);  // 2 working + 2 graceful-failure
    }

    [Fact(DisplayName = "BR-EXTEND-010 — /otel up bookends the start; /otel down bookends the end")]
    public async Task Lifecycle_Bookends_Are_First_And_Last_Invoke_Steps()
    {
        var d = new OtelDomainDemo();
        var ctx = new DemoContext("test-session");
        var steps = await d.Demos.First(c => c.IsDefault).Plan(ctx, CancellationToken.None);
        var invokes = steps.Where(s => s.Kind == "invoke").ToArray();

        Assert.Equal(("otel", "up"), (invokes.First().Skill, invokes.First().Args));
        Assert.Equal(("otel", "down"), (invokes.Last().Skill, invokes.Last().Args));
    }

    [Fact(DisplayName = "BR-EXTEND-010 — observe steps target output/telemetry.jsonl")]
    public async Task Observe_Steps_Target_Telemetry_Jsonl()
    {
        var d = new OtelDomainDemo();
        var ctx = new DemoContext("test-session");
        var steps = await d.Demos.First(c => c.IsDefault).Plan(ctx, CancellationToken.None);

        foreach (var s in steps.Where(s => s.Kind == "observe"))
            Assert.Equal(OtelDomainDemo.TelemetryFile, s.ObserveTarget);
    }
}
