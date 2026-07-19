using System.Globalization;
using AgentCake;
using Xunit;

public class UsageMathTests
{
    private static DateTime D(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture);

    // ---- weekly reset ----

    [Theory]
    [InlineData("2026-06-24T10:00", DayOfWeek.Monday, 0, "2026-06-22T00:00")]  // Wed -> last Mon 00:00
    [InlineData("2026-06-22T00:00", DayOfWeek.Monday, 0, "2026-06-22T00:00")]  // exactly at reset
    [InlineData("2026-06-22T09:00", DayOfWeek.Monday, 12, "2026-06-15T12:00")] // before today's 12:00 reset
    [InlineData("2026-06-21T23:00", DayOfWeek.Monday, 0, "2026-06-15T00:00")]  // Sun before Mon reset
    [InlineData("2026-06-24T10:00", DayOfWeek.Wednesday, 8, "2026-06-24T08:00")] // same-day, after hour
    public void MostRecentReset_returns_expected(string now, DayOfWeek day, int hour, string expected)
        => Assert.Equal(D(expected), UsageMath.MostRecentReset(D(now), day, hour));

    [Fact]
    public void MostRecentReset_clamps_out_of_range_hour()
        => Assert.Equal(23, UsageMath.MostRecentReset(D("2026-06-24T10:00"), DayOfWeek.Monday, 99).Hour);

    // ---- 5-hour window blocks ----

    [Fact]
    public void Window_active_uses_first_record_and_floored_end()
    {
        var ts = new[] { D("2026-06-24T08:10"), D("2026-06-24T08:30"), D("2026-06-24T09:50") };
        var lb = UsageMath.LastBlock(ts);
        Assert.NotNull(lb);
        Assert.Equal(D("2026-06-24T08:10"), lb!.Value.FirstTs);   // membership boundary
        Assert.Equal(D("2026-06-24T13:00"), lb.Value.EndsAt);     // floor(08:10)+5h
        Assert.True(D("2026-06-24T10:00") < lb.Value.EndsAt);     // still active at 10:00
    }

    [Fact]
    public void Window_is_inactive_once_past_end()
    {
        var ts = new[] { D("2026-06-24T08:10"), D("2026-06-24T09:50") };
        var lb = UsageMath.LastBlock(ts)!.Value;
        Assert.Equal(D("2026-06-24T13:00"), lb.EndsAt);
        Assert.False(D("2026-06-24T20:00") < lb.EndsAt);          // idle -> fresh window
    }

    [Fact]
    public void Gap_over_five_hours_opens_a_new_block()
    {
        var ts = new[] { D("2026-06-24T08:00"), D("2026-06-24T14:30") };
        var lb = UsageMath.LastBlock(ts)!.Value;
        Assert.Equal(D("2026-06-24T14:30"), lb.FirstTs);
        Assert.Equal(D("2026-06-24T19:00"), lb.EndsAt);
    }

    [Fact]
    public void Continuous_activity_past_five_hours_rolls_to_new_block()
    {
        var ts = new List<DateTime>();
        for (int h = 8; h <= 14; h++) ts.Add(D($"2026-06-24T{h:00}:00"));
        var lb = UsageMath.LastBlock(ts)!.Value;
        Assert.Equal(D("2026-06-24T13:00"), lb.FirstTs);          // 13:00 trips pastWindow
        Assert.Equal(D("2026-06-24T18:00"), lb.EndsAt);
    }

    [Fact]
    public void Unsorted_input_is_handled()
    {
        var ts = new[] { D("2026-06-24T09:50"), D("2026-06-24T08:10"), D("2026-06-24T08:30") };
        var lb = UsageMath.LastBlock(ts)!.Value;
        Assert.Equal(D("2026-06-24T08:10"), lb.FirstTs);
    }

    [Fact]
    public void Empty_input_returns_null()
        => Assert.Null(UsageMath.LastBlock(Array.Empty<DateTime>()));
}
