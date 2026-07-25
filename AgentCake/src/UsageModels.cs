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

public sealed record UsageSnapshot(ServiceUsage Codex, ServiceUsage Claude, DateTime GeneratedAt);

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
        if (ordered.Count < 2) return null;

        var newest = ordered[^1];
        // A one-day lookback smooths out a single short burst without mixing a
        // previous weekly window into the pace calculation.
        var baseline = ordered.FirstOrDefault(sample => sample.RecordedAt >= newest.RecordedAt.AddHours(-24)
            && sample.RecordedAt <= newest.RecordedAt.AddMinutes(-10));
        if (baseline is null) return null;

        double elapsedHours = (newest.RecordedAt - baseline.RecordedAt).TotalHours;
        double usedDelta = newest.UsedPercent - baseline.UsedPercent;
        if (elapsedHours <= 0 || usedDelta < 0.25) return null;

        double burnRate = usedDelta / elapsedHours;
        double sustainableRate = remaining / remainingHours;
        return sustainableRate <= 0 ? null : (burnRate, burnRate / sustainableRate);
    }
}
