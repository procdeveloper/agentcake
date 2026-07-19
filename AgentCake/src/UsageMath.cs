namespace AgentCake;

/// <summary>
/// Pure, dependency-free usage math (no WinForms/IO), so it can be unit-tested directly.
/// See AgentCake.Tests/UsageMathTests.cs.
/// </summary>
internal static class UsageMath
{
    internal static readonly TimeSpan WindowLen = TimeSpan.FromHours(5);

    /// <summary>Most recent weekly-reset instant at or before <paramref name="now"/>.</summary>
    internal static DateTime MostRecentReset(DateTime now, DayOfWeek day, int hour)
    {
        hour = Math.Clamp(hour, 0, 23);
        int diff = ((int)now.DayOfWeek - (int)day + 7) % 7;
        DateTime candidate = now.Date.AddDays(-diff).AddHours(hour);
        if (candidate > now) candidate = candidate.AddDays(-7);
        return candidate;
    }

    internal static DateTime FloorToHour(DateTime dt)
        => new(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind);

    /// <summary>
    /// Identify the final ccusage-style 5-hour block from a set of activity timestamps.
    /// A block starts at the first activity (floored to the hour) and lasts 5h; a gap longer
    /// than 5h, or activity past the 5h mark, opens a new block. Returns the timestamp of the
    /// final block's first record and when that block ends (floored-start + 5h), or null when
    /// there is no activity. The active window's members are exactly the records with
    /// timestamp &gt;= <c>FirstTs</c>; the window is "active" while <c>now &lt; EndsAt</c>.
    /// </summary>
    internal static (DateTime FirstTs, DateTime EndsAt)? LastBlock(IEnumerable<DateTime> timestamps)
    {
        var ord = timestamps.OrderBy(t => t).ToList();
        if (ord.Count == 0) return null;

        DateTime blockStart = FloorToHour(ord[0]);
        DateTime firstTs = ord[0];
        DateTime prev = ord[0];

        for (int i = 1; i < ord.Count; i++)
        {
            DateTime t = ord[i];
            bool gapTooBig = (t - prev) > WindowLen;
            bool pastWindow = t >= blockStart + WindowLen;
            if (gapTooBig || pastWindow)
            {
                blockStart = FloorToHour(t);
                firstTs = t;
            }
            prev = t;
        }
        return (firstTs, blockStart + WindowLen);
    }
}
