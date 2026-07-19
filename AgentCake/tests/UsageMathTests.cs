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
    public void Claude_Desktop_uses_the_latest_seven_day_sample()
    {
        const string json = """{ "version": 2, "samples": [{ "t": 1784447000000, "u": { "fh": 11, "sd": 83 } }, { "t": 1784447300000, "u": { "fh": 15, "sd": 84 } }] }""";
        Assert.True(UsageParsers.TryParseClaudeDesktopWeekly(json, out var usage));
        Assert.Equal("Claude", usage.Service);
        Assert.Equal(84, usage.UsedPercent);
        Assert.Equal(16, usage.RemainingPercent);
        Assert.Null(usage.ResetsAt);
    }
}
