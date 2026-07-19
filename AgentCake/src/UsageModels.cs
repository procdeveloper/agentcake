namespace AgentCake;

/// <summary>One assistant turn pulled from a Claude Code JSONL transcript.</summary>
public readonly record struct UsageRecord(
    DateTime TimestampLocal,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens)
{
    /// <summary>All tokens.</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;

    /// <summary>Tokens excluding cache reads (cache reads count far more cheaply toward limits).</summary>
    public long TotalTokensExclCacheRead => InputTokens + OutputTokens + CacheCreationTokens;
}

/// <summary>Rolled-up totals for a period (current 5h window, this week, etc.).</summary>
public sealed class UsageTotals
{
    public long Input;
    public long Output;
    public long CacheCreation;
    public long CacheRead;
    public int Turns;

    public long Total => Input + Output + CacheCreation + CacheRead;
    public long TotalExclCacheRead => Input + Output + CacheCreation;

    public void Add(in UsageRecord r)
    {
        Input += r.InputTokens;
        Output += r.OutputTokens;
        CacheCreation += r.CacheCreationTokens;
        CacheRead += r.CacheReadTokens;
        Turns++;
    }

    /// <summary>The token count used for gauges, honoring the cache-read setting.</summary>
    public long Billable(bool countCacheReads) => countCacheReads ? Total : TotalExclCacheRead;
}

/// <summary>Everything the UI needs after a refresh.</summary>
public sealed class UsageSnapshot
{
    public UsageTotals CurrentWindow { get; init; } = new();
    public UsageTotals Week { get; init; } = new();

    /// <summary>End time of the active 5-hour window, or null if no window is active (idle).</summary>
    public DateTime? WindowEndsAt { get; init; }

    /// <summary>Start of the current weekly period (most recent weekly reset).</summary>
    public DateTime WeekStartedAt { get; init; }
    public DateTime WeekResetsAt { get; init; }

    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public string DataDir { get; init; } = "";
    public bool DataDirExists { get; init; }
}
