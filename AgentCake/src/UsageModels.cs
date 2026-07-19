namespace AgentCake;

public sealed record ServiceUsage(string Service, double? UsedPercent, DateTime? ResetsAt, string Detail)
{
    public int? RemainingPercent => UsedPercent is null
        ? null
        : (int)Math.Round(Math.Clamp(100d - UsedPercent.Value, 0d, 100d));

    public static ServiceUsage Unavailable(string service, string detail) => new(service, null, null, detail);
}

public sealed record UsageSnapshot(ServiceUsage Codex, ServiceUsage Claude, DateTime GeneratedAt);