using AgentCake;
using Xunit;

public class UsageParserTests
{
    [Fact]
    public void Codex_uses_the_longest_live_limit_window()
    {
        const string json = """{ "payload": { "rate_limits": { "limit_id": "codex", "primary": { "used_percent": 42, "window_minutes": 10080, "resets_at": 1784991002 }, "secondary": { "used_percent": 5, "window_minutes": 300 } } } }""";
        Assert.True(UsageParsers.TryParseCodexWeekly(json, out var usage));
        Assert.Equal("Codex", usage.Service);
        Assert.Equal(42, usage.UsedPercent);
        Assert.Equal(58, usage.RemainingPercent);
        Assert.NotNull(usage.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(7), usage.WeeklyWindow);
    }

    [Fact]
    public void Codex_accepts_camel_case_remaining_percent_and_second_windows()
    {
        const string json = """{ "event": { "rateLimits": { "limitId": "codex", "short": { "usagePercent": 81, "windowSeconds": 18000 }, "weeklyAllowance": { "remainingPercent": 91, "windowSeconds": 604800, "nextResetAt": "1785564999" } } } }""";
        Assert.True(UsageParsers.TryParseCodexWeekly(json, out var usage));
        Assert.Equal(9, usage.UsedPercent);
        Assert.Equal(91, usage.RemainingPercent);
        Assert.Equal(TimeSpan.FromDays(7), usage.WeeklyWindow);
        Assert.NotNull(usage.ResetsAt);
    }

    [Fact]
    public void Codex_reader_falls_back_when_a_large_jsonl_line_exceeds_the_tail()
    {
        string sessionsDir = Path.Combine(Path.GetTempPath(), "AgentCake.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionsDir);
        try
        {
            string padding = new('x', 300_000);
            string json = $$"""{ "timestamp": "2026-07-25T16:33:06Z", "payload": { "info": { "rate_limits": { "limit_id": "codex", "primary": { "used_percent": 12, "window_minutes": 10080, "resets_at": 1785564999 } }, "padding": "{{padding}}" } } }""";
            File.WriteAllText(Path.Combine(sessionsDir, "large-session.jsonl"), json);

            var reader = new UsageReader(() => new AppSettings { CodexSessionsDir = sessionsDir });
            var usage = reader.Scan().Codex;

            Assert.Equal(12, usage.UsedPercent);
            Assert.Equal(88, usage.RemainingPercent);
            Assert.Equal(TimeSpan.FromDays(7), usage.WeeklyWindow);
        }
        finally
        {
            Directory.Delete(sessionsDir, recursive: true);
        }
    }

    [Fact]
    public void Codex_ignores_model_specific_allowances()
    {
        const string json = """{ "timestamp": "2026-07-25T16:35:06Z", "payload": { "rate_limits": { "limit_id": "codex_bengalfox", "limit_name": "GPT-5.3-Codex-Spark", "primary": { "used_percent": 0, "window_minutes": 10080 } } } }""";
        Assert.False(UsageParsers.TryParseCodexWeekly(json, out _));
    }

    [Fact]
    public void Codex_reader_uses_event_time_not_file_write_time()
    {
        string sessionsDir = Path.Combine(Path.GetTempPath(), "AgentCake.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionsDir);
        try
        {
            string olderEvent = """{ "timestamp": "2026-07-25T16:31:00Z", "payload": { "rate_limits": { "limit_id": "codex", "primary": { "used_percent": 0, "window_minutes": 10080 } } } }""";
            string newerEvent = """{ "timestamp": "2026-07-25T16:33:00Z", "payload": { "rate_limits": { "limit_id": "codex", "primary": { "used_percent": 15, "window_minutes": 10080 } } } }""";
            string newerFileTimestamp = Path.Combine(sessionsDir, "newer-file.jsonl");
            string olderFileTimestamp = Path.Combine(sessionsDir, "older-file.jsonl");
            File.WriteAllText(newerFileTimestamp, olderEvent);
            File.WriteAllText(olderFileTimestamp, newerEvent);
            File.SetLastWriteTimeUtc(newerFileTimestamp, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(olderFileTimestamp, DateTime.UtcNow.AddHours(-1));

            var usage = new UsageReader(() => new AppSettings { CodexSessionsDir = sessionsDir }).Scan().Codex;

            Assert.Equal(15, usage.UsedPercent);
            Assert.Equal(85, usage.RemainingPercent);
        }
        finally
        {
            Directory.Delete(sessionsDir, recursive: true);
        }
    }

    [Fact]
    public void Burn_pace_compares_recent_usage_with_the_remaining_week()
    {
        DateTime reset = DateTime.Now.AddHours(50);
        DateTimeOffset now = DateTimeOffset.Now;
        var current = new ServiceUsage("Codex", 50, reset, "Live", WeeklyWindow: TimeSpan.FromDays(7));
        var samples = new[]
        {
            new UsageSample(now.AddHours(-2), 47, reset),
            new UsageSample(now, 50, reset)
        };

        var pace = UsagePace.Estimate(samples, current);

        Assert.NotNull(pace);
        Assert.Equal(1.5, pace!.Value.BurnRatePercentPerHour, 3);
        Assert.Equal(1.5, pace.Value.BurnPaceRatio, 3);
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

    [Fact]
    public void Claude_Desktop_does_not_invent_a_reset_from_usage_history()
    {
        const long resetSample = 1784833341006;
        string json = $$"""{ "version": 2, "samples": [{ "t": {{resetSample - 300000}}, "u": { "fh": 94, "sd": 94 } }, { "t": {{resetSample}}, "u": { "fh": 0, "sd": 0 } }, { "t": {{resetSample + 300000}}, "u": { "fh": 33, "sd": 15 } }] }""";

        Assert.True(UsageParsers.TryParseClaudeDesktopWeekly(json, out var usage));
        Assert.Equal(15, usage.UsedPercent);
        Assert.Equal(33, usage.FiveHourUsedPercent);
        Assert.Equal(TimeSpan.FromDays(7), usage.WeeklyWindow);
        Assert.Null(usage.ResetsAt);
        Assert.Null(usage.FiveHourResetsAt);
    }

    [Fact]
    public void Claude_Desktop_calculates_throttle_pace_when_given_a_live_weekly_reset()
    {
        DateTime reset = DateTime.Now.AddDays(2);
        DateTimeOffset now = DateTimeOffset.Now;
        string json = $$"""{ "version": 2, "samples": [{ "t": {{now.AddHours(-2).ToUnixTimeMilliseconds()}}, "u": { "sd": 47 } }, { "t": {{now.ToUnixTimeMilliseconds()}}, "u": { "sd": 50 } }] }""";

        Assert.True(UsageParsers.TryParseClaudeDesktopWeekly(json, out var usage, reset));

        Assert.Equal(reset, usage.ResetsAt);
        Assert.NotNull(usage.BurnPaceRatio);
        Assert.NotNull(usage.BurnRatePercentPerHour);
    }

    [Fact]
    public void Claude_Desktop_reads_real_reset_timestamps_from_its_live_rate_limit_log()
    {
        const string line = "2026-07-26 12:22:59 [error] Uncaught (in promise) Error: {\"type\":\"exceeded_limit\",\"windows\":{\"5h\":{\"resets_at\":1785069600},\"7d\":{\"resets_at\":1785438000}}}";

        var resets = UsageParsers.ReadClaudeDesktopLiveResets(new[] { line });

        Assert.NotNull(resets);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785069600).LocalDateTime, resets!.FiveHourResetsAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785438000).LocalDateTime, resets.WeeklyResetsAt);
    }
}
