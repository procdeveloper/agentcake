namespace AgentCake;

public sealed record ServiceUsage(
    string Service,
    double? UsedPercent,
    DateTime? ResetsAt,
    string Detail,
    double? FiveHourUsedPercent = null,
    DateTime? FiveHourResetsAt = null,
    TimeSpan? WeeklyWindow = null,
    double? BurnRatePercentPerHour = null,
    double? BurnPaceRatio = null)
{
    public int? RemainingPercent => UsedPercent is null
        ? null
        : (int)Math.Round(Math.Clamp(100d - UsedPercent.Value, 0d, 100d));

    public int? FiveHourRemainingPercent => FiveHourUsedPercent is null
        ? null
        : (int)Math.Round(Math.Clamp(100d - FiveHourUsedPercent.Value, 0d, 100d));

    public static ServiceUsage Unavailable(string service, string detail) => new(service, null, null, detail);
}

public sealed record UsageSnapshot(ServiceUsage Codex, ServiceUsage CodexSpark, ServiceUsage Claude, DateTime GeneratedAt);

internal sealed record UsageSample(DateTimeOffset RecordedAt, double UsedPercent, DateTime? ResetsAt);

internal static class UsagePace
{
    public static (double BurnRatePercentPerHour, double BurnPaceRatio)? Estimate(IEnumerable<UsageSample> samples, ServiceUsage current)
    {
        if (current.UsedPercent is not { } currentUsed || current.ResetsAt is not { } resetsAt) return null;
        double remaining = Math.Max(0, 100 - currentUsed);
        double remainingHours = (resetsAt - DateTime.Now).TotalHours;
        if (remaining <= 0 || remainingHours <= 0) return null;

        var ordered = samples
            .Where(sample => sample.RecordedAt != DateTimeOffset.MinValue
                && sample.UsedPercent <= currentUsed
                && sample.ResetsAt == resetsAt)
            .OrderBy(sample => sample.RecordedAt)
            .ToList();
        if (ordered.Count < 2)
        {
            // Right after a weekly reset there is often only one Codex event in
            // the new window. The reset time and window length still give us a
            // real average from the start of that window, so keep the gauge at
            // a useful 0.0x/starting value instead of rendering it as unknown.
            if (current.WeeklyWindow is not { } weeklyWindow) return null;
            double elapsedSinceResetHours = (DateTime.Now - (resetsAt - weeklyWindow)).TotalHours;
            if (elapsedSinceResetHours <= 0) return null;

            double burnRateSinceReset = currentUsed / elapsedSinceResetHours;
            double sustainableRateSinceReset = remaining / remainingHours;
            return sustainableRateSinceReset <= 0 ? null : (burnRateSinceReset, burnRateSinceReset / sustainableRateSinceReset);
        }

        var newest = ordered[^1];
        // A one-day lookback smooths out a single short burst without mixing a
        // previous weekly window into the pace calculation.
        var baseline = ordered.FirstOrDefault(sample => sample.RecordedAt >= newest.RecordedAt.AddHours(-24)
            && sample.RecordedAt <= newest.RecordedAt.AddMinutes(-10));
        if (baseline is null) return null;

        double elapsedHours = (newest.RecordedAt - baseline.RecordedAt).TotalHours;
        double usedDelta = newest.UsedPercent - baseline.UsedPercent;
        if (elapsedHours <= 0) return null;
        if (usedDelta < 0.25)
        {
            // A reset can produce several identical 0%-usage events before
            // Codex spends anything. Keep the dial explicit at 0.0x instead
            // of treating that real, quiet state as an unknown rate.
            if (current.WeeklyWindow is not { } weeklyWindow) return null;
            double elapsedSinceResetHours = (DateTime.Now - (resetsAt - weeklyWindow)).TotalHours;
            if (elapsedSinceResetHours <= 0) return null;
            double burnRateSinceReset = currentUsed / elapsedSinceResetHours;
            double sustainableRateSinceReset = remaining / remainingHours;
            return sustainableRateSinceReset <= 0 ? null : (burnRateSinceReset, burnRateSinceReset / sustainableRateSinceReset);
        }

        double burnRate = usedDelta / elapsedHours;
        double sustainableRate = remaining / remainingHours;
        return sustainableRate <= 0 ? null : (burnRate, burnRate / sustainableRate);
    }
}
