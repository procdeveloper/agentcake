using AgentCake;
using Xunit;

public class UsageParserTests
{
    [Fact]
    public void Codex_uses_the_longest_live_limit_window()
    {
        const string json = """{ "payload": { "rate_limits": { "primary": { "used_percent": 42, "window_minutes": 10080, "resets_at": 1784991002 }, "secondary": { "used_percent": 5, "window_minutes": 300 } } } }""";
        Assert.True(UsageParsers.TryParseCodexWeekly(json, out var usage));
        Assert.Equal("Codex", usage.Service);
        Assert.Equal(42, usage.UsedPercent);
        Assert.Equal(58, usage.RemainingPercent);
        Assert.NotNull(usage.ResetsAt);
    }

    [Fact]
    public void Claude_uses_the_seven_day_status_limit()
    {
        const string json = """{ "rate_limits": { "five_hour": { "used_percentage": 11 }, "seven_day": { "used_percentage": 83, "resets_at": "2026-07-22T12:00:00Z" } } }""";
        Assert.True(UsageParsers.TryParseClaudeWeekly(json, out var usage));
        Assert.Equal("Claude", usage.Service);
        Assert.Equal(83, usage.UsedPercent);
        Assert.Equal(17, usage.RemainingPercent);
        Assert.NotNull(usage.ResetsAt);
    }
}