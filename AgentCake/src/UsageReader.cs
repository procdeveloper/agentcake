using System.Text;
using System.Text.Json;

namespace AgentCake;

public sealed class UsageReader
{
    private readonly Func<AppSettings> _settings;
    private ServiceUsage? _lastCodexUsage;
    private ServiceUsage? _lastCodexSparkUsage;

    public UsageReader(Func<AppSettings> settings) => _settings = settings;

    public UsageSnapshot Scan()
    {
        var cfg = _settings();
        var codex = ReadCodex(cfg.ResolveCodexSessionsDir());
        return new UsageSnapshot(codex.OtherModels, codex.Spark, ReadClaudeDesktop(cfg.ResolveClaudeDesktopUsagePath(), cfg.ResolveClaudeDesktopLogPath()), DateTime.Now);
    }

    private (ServiceUsage OtherModels, ServiceUsage Spark) ReadCodex(string sessionsDir)
    {
        if (!Directory.Exists(sessionsDir))
            return (_lastCodexUsage ?? ServiceUsage.Unavailable("Codex other", "Codex session folder was not found."),
                _lastCodexSparkUsage ?? ServiceUsage.Unavailable("Codex Spark", "No live Spark allowance record has been written yet."));

        try
        {
            var files = Directory.EnumerateFiles(sessionsDir, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                // A plan change can start a fresh session before it has emitted a
                // limit event. Search a useful history instead of treating the
                // newest dozen session files as the complete account history.
                .Take(80);

            var records = new List<CodexUsageRecord>();
            foreach (var file in files)
            {
                var fileRecords = FindCodexRecords(TailLines(file.FullName)).ToList();

                // Session JSONL records can contain a very large prompt or tool
                // result. If the rate-limit record lives near the start of one of
                // those lines, a byte-tail begins mid-JSON and cannot parse it.
                // Only then fall back to a complete, shared-read scan of this file.
                if (fileRecords.Count == 0) fileRecords = FindCodexRecords(AllLines(file.FullName)).ToList();

                // Files can be touched out of chronological order. The event time,
                // not the filesystem write time, is the authority for live usage.
                records.AddRange(fileRecords);
            }

            var other = BuildCodexUsage(records.Where(record => string.Equals(record.LimitId, "codex", StringComparison.OrdinalIgnoreCase)));
            var spark = BuildCodexUsage(records.Where(record =>
                record.LimitId.Contains("spark", StringComparison.OrdinalIgnoreCase)
                || record.LimitId.Contains("bengalfox", StringComparison.OrdinalIgnoreCase)
                || record.Usage.Service.Contains("Spark", StringComparison.OrdinalIgnoreCase)));
            if (other is not null)
            {
                _lastCodexUsage = other;
            }
            if (spark is not null)
            {
                _lastCodexSparkUsage = spark;
            }

            var codex = other ?? _lastCodexUsage
                ?? ServiceUsage.Unavailable("Codex other", "No live weekly account-limit record has been written yet.");
            var sparkUsage = spark ?? _lastCodexSparkUsage
                ?? ServiceUsage.Unavailable("Codex Spark", "No live Spark allowance record has been written yet.");

            return (codex, sparkUsage);
        }
        catch (Exception exception) { CrashLog.Write("Codex usage scan failed", exception); }

        return (_lastCodexUsage ?? ServiceUsage.Unavailable("Codex other", "No live weekly account-limit record has been written yet."),
            _lastCodexSparkUsage ?? ServiceUsage.Unavailable("Codex Spark", "No live Spark allowance record has been written yet."));
    }

    private static ServiceUsage? BuildCodexUsage(IEnumerable<CodexUsageRecord> source)
    {
        var records = source.ToList();
        var newest = records.OrderByDescending(record => record.RecordedAt).FirstOrDefault();
        if (newest is null) return null;
        var pace = UsagePace.Estimate(records.Select(record => new UsageSample(record.RecordedAt, record.Usage.UsedPercent ?? 0, record.Usage.ResetsAt)), newest.Usage);
        return newest.Usage with { BurnRatePercentPerHour = pace?.BurnRatePercentPerHour, BurnPaceRatio = pace?.BurnPaceRatio };
    }

    private static ServiceUsage ReadClaudeDesktop(string historyPath, string logPath)
    {
        if (!File.Exists(historyPath))
            return ServiceUsage.Unavailable("Claude", "Claude Desktop plan-usage history was not found. Open Claude Desktop and sign in.");

        try
        {
            string historyJson = File.ReadAllText(historyPath);
            var realResets = File.Exists(logPath)
                ? UsageParsers.ReadClaudeDesktopLiveResets(TailLines(logPath))
                : null;
            DateTime? fiveHourReset = realResets?.FiveHourResetsAt > DateTime.Now ? realResets.FiveHourResetsAt : null;
            DateTime? liveWeeklyReset = realResets?.WeeklyResetsAt > DateTime.Now ? realResets.WeeklyResetsAt : null;
            DateTime? observedWeeklyReset = UsageParsers.ReadClaudeDesktopObservedWeeklyReset(historyJson);
            DateTime? weeklyReset = liveWeeklyReset ?? observedWeeklyReset;

            // Give the pace calculation the same authoritative weekly reset as
            // the rendered row. Without it, Claude's historical samples have no
            // reset field to match and the throttle gauge is deliberately blank.
            if (!UsageParsers.TryParseClaudeDesktopWeekly(historyJson, out var usage, weeklyReset))
                return ServiceUsage.Unavailable("Claude", "Claude Desktop has not recorded a weekly usage value yet.");

            return usage with
            {
                ResetsAt = weeklyReset,
                FiveHourResetsAt = fiveHourReset,
                Detail = liveWeeklyReset is not null
                    ? "Live Claude Desktop plan usage; weekly reset time comes from Claude Desktop's live rate-limit response."
                    : observedWeeklyReset is not null
                        ? "Live Claude Desktop plan usage; weekly reset was observed in Claude Desktop's own usage history."
                        : "Live Claude Desktop plan usage; reset time is unavailable until Claude writes a live rate-limit response."
            };
        }
        catch
        {
            return ServiceUsage.Unavailable("Claude", "Claude Desktop usage history is being updated; retrying shortly.");
        }
    }

    private static IEnumerable<string> TailLines(string path)
    {
        const int maxBytes = 256 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        if (start > 0)
        {
            int firstNewline = text.IndexOf('\n');
            text = firstNewline >= 0 ? text[(firstNewline + 1)..] : "";
        }
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> AllLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }
    }

    private static IEnumerable<CodexUsageRecord> FindCodexRecords(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!line.Contains("\"rate_limits\"", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("\"rateLimits\"", StringComparison.OrdinalIgnoreCase)) continue;
            if (!UsageParsers.TryParseCodexLimits(line, out var usages, out var recordedAt)) continue;
            foreach (var usage in usages) yield return new CodexUsageRecord(usage.LimitId, usage.Usage, recordedAt);
        }
    }
}

internal sealed record CodexUsageRecord(string LimitId, ServiceUsage Usage, DateTimeOffset RecordedAt);
internal sealed record CodexLimitUsage(string LimitId, ServiceUsage Usage);

public static class UsageParsers
{
    public static bool TryParseCodexWeekly(string json, out ServiceUsage usage)
        => TryParseCodexWeekly(json, out usage, out _);

    internal static bool TryParseCodexWeekly(string json, out ServiceUsage usage, out DateTimeOffset recordedAt)
    {
        if (!TryParseCodexLimits(json, out var usages, out recordedAt))
        {
            usage = ServiceUsage.Unavailable("Codex other", "No live Codex limit.");
            return false;
        }
        usage = usages.FirstOrDefault(candidate => string.Equals(candidate.LimitId, "codex", StringComparison.OrdinalIgnoreCase))?.Usage
            ?? ServiceUsage.Unavailable("Codex other", "No live Codex limit.");
        return usage.UsedPercent is not null;
    }

    internal static bool TryParseCodexLimits(string json, out IReadOnlyList<CodexLimitUsage> usages, out DateTimeOffset recordedAt)
    {
        usages = Array.Empty<CodexLimitUsage>();
        recordedAt = DateTimeOffset.MinValue;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var parsed = new List<CodexLimitUsage>();
            foreach (var limits in FindNamedObjects(doc.RootElement, "rate_limits", "rateLimits", "rate_limit", "rateLimit", "limits"))
            {
                if ((!TryString(limits, "limit_id", out var limitId) && !TryString(limits, "limitId", out limitId)) || string.IsNullOrWhiteSpace(limitId)) continue;
                var candidates = new List<(JsonElement Window, double Minutes, int NameScore)>();
                foreach (var property in limits.EnumerateObject())
                {
                    var window = property.Value;
                    if (window.ValueKind == JsonValueKind.Object && TryReadUsedPercent(window, out _))
                        candidates.Add((window, ReadWindowMinutes(window), WindowNameScore(property.Name)));
                }
                if (candidates.Count == 0) continue;

                var weekly = candidates.OrderByDescending(candidate => candidate.Minutes).ThenByDescending(candidate => candidate.NameScore).First();
                if (!TryReadUsedPercent(weekly.Window, out var used)) continue;
                TryString(limits, "limit_name", out var limitName);
                if (string.IsNullOrWhiteSpace(limitName)) TryString(limits, "limitName", out limitName);
                string service = string.Equals(limitId, "codex", StringComparison.OrdinalIgnoreCase)
                    ? "Codex other"
                    : limitName?.Contains("Spark", StringComparison.OrdinalIgnoreCase) == true || limitId.Contains("spark", StringComparison.OrdinalIgnoreCase) || limitId.Contains("bengalfox", StringComparison.OrdinalIgnoreCase)
                        ? "Codex Spark"
                        : $"Codex {limitName ?? limitId}";
                TimeSpan? weeklyWindow = weekly.Minutes > 0 ? TimeSpan.FromMinutes(weekly.Minutes) : null;
                parsed.Add(new CodexLimitUsage(limitId, new ServiceUsage(service, used, ReadReset(weekly.Window), "Live Codex model allowance", WeeklyWindow: weeklyWindow)));
            }
            usages = parsed;
            recordedAt = ReadEventTimestamp(doc.RootElement);
            return parsed.Count > 0;
        }
        catch { return false; }
    }

    public static bool TryParseClaudeDesktopWeekly(string json, out ServiceUsage usage, DateTime? weeklyResetsAt = null)
    {
        usage = ServiceUsage.Unavailable("Claude", "No Claude Desktop weekly limit.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
                return false;

            DateTime? fiveHourResetsAt = null;
            var weeklySamples = new List<UsageSample>();
            foreach (var sample in samples.EnumerateArray())
            {
                if (sample.TryGetProperty("u", out var usageValues) && usageValues.ValueKind == JsonValueKind.Object
                    && TryNumber(usageValues, "sd", out var sampleUsed)
                    && TryNumber(sample, "t", out var sampleTimestamp))
                {
                    try { weeklySamples.Add(new UsageSample(DateTimeOffset.FromUnixTimeMilliseconds((long)sampleTimestamp), sampleUsed, weeklyResetsAt)); }
                    catch { }
                }
            }

            for (var index = samples.GetArrayLength() - 1; index >= 0; index--)
            {
                var sample = samples[index];
                if (sample.TryGetProperty("u", out var usageValues) && usageValues.ValueKind == JsonValueKind.Object
                    && TryNumber(usageValues, "sd", out var used))
                {
                    double? fiveHourUsed = TryNumber(usageValues, "fh", out var parsedFiveHourUsed)
                        ? parsedFiveHourUsed
                        : null;
                    var liveUsage = new ServiceUsage("Claude", used, weeklyResetsAt, weeklyResetsAt is null
                        ? "Live Claude Desktop plan usage"
                        : "Live Claude Desktop plan usage; reset time requires Claude Desktop's live rate-limit response.",
                        fiveHourUsed,
                        fiveHourResetsAt,
                        WeeklyWindow: TimeSpan.FromDays(7));
                    var pace = UsagePace.Estimate(weeklySamples, liveUsage);
                    usage = liveUsage with
                    {
                        BurnRatePercentPerHour = pace?.BurnRatePercentPerHour,
                        BurnPaceRatio = pace?.BurnPaceRatio
                    };
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    public static DateTime? ReadClaudeDesktopObservedWeeklyReset(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
                return null;

            double? previousUsed = null;
            DateTime? nextReset = null;
            foreach (var sample in samples.EnumerateArray())
            {
                if (!sample.TryGetProperty("u", out var usageValues) || usageValues.ValueKind != JsonValueKind.Object
                    || !TryNumber(usageValues, "sd", out var used)
                    || !TryNumber(sample, "t", out var timestamp)) continue;

                // Claude Desktop does not retain a forward reset timestamp once
                // a window is healthy again, but its own history records the
                // rollover as a large weekly-usage drop. This is a fallback to
                // that observed event, never a guessed clock or a small correction.
                if (previousUsed is { } previous && used < previous - 5)
                {
                    try
                    {
                        DateTime candidate = DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).LocalDateTime.AddDays(7);
                        if (candidate > DateTime.Now) nextReset = candidate;
                    }
                    catch { }
                }

                previousUsed = used;
            }
            return nextReset;
        }
        catch { return null; }
    }

    // Claude Desktop logs the server's actual reset timestamps with a live
    // rate-limit response; its plan-usage history intentionally contains only percentages.
    public static ClaudeLiveResets? ReadClaudeDesktopLiveResets(IEnumerable<string> lines)
    {
        foreach (var line in lines.Reverse())
        {
            int jsonStart = line.LastIndexOf("Error: {", StringComparison.Ordinal);
            if (jsonStart < 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line[(jsonStart + "Error: ".Length)..]);
                if (!doc.RootElement.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Object) continue;
                DateTime? fiveHour = windows.TryGetProperty("5h", out var fiveHourWindow) ? ReadReset(fiveHourWindow) : null;
                DateTime? weekly = windows.TryGetProperty("7d", out var weeklyWindow) ? ReadReset(weeklyWindow) : null;
                if (fiveHour is not null || weekly is not null) return new ClaudeLiveResets(fiveHour, weekly);
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<JsonElement> FindNamedObjects(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object && names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                    yield return property.Value.Clone();
                foreach (var found in FindNamedObjects(property.Value, names))
                    yield return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var found in FindNamedObjects(item, names))
                    yield return found;
            }
        }
    }

    private static bool TryNumber(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!TryGetProperty(element, name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Number) return property.TryGetDouble(out value);
        return property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        value = null;
        return TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String && (value = property.GetString()) is not null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryReadUsedPercent(JsonElement window, out double used)
    {
        foreach (var name in new[] { "used_percent", "usedPercent", "usage_percent", "usagePercent", "percent_used", "percentUsed" })
        {
            if (TryNumber(window, name, out used) && used is >= 0 and <= 100) return true;
        }

        foreach (var name in new[] { "remaining_percent", "remainingPercent", "percent_remaining", "percentRemaining" })
        {
            if (TryNumber(window, name, out var remaining) && remaining is >= 0 and <= 100)
            {
                used = 100 - remaining;
                return true;
            }
        }

        used = 0;
        return false;
    }

    private static double ReadWindowMinutes(JsonElement window)
    {
        foreach (var name in new[] { "window_minutes", "windowMinutes", "duration_minutes", "durationMinutes" })
            if (TryNumber(window, name, out var minutes) && minutes > 0) return minutes;
        foreach (var name in new[] { "window_seconds", "windowSeconds", "duration_seconds", "durationSeconds" })
            if (TryNumber(window, name, out var seconds) && seconds > 0) return seconds / 60;
        return 0;
    }

    private static int WindowNameScore(string name) => name.Contains("week", StringComparison.OrdinalIgnoreCase) || name.Contains("seven", StringComparison.OrdinalIgnoreCase)
        ? 2
        : name.Contains("primary", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    private static DateTimeOffset ReadEventTimestamp(JsonElement root)
    {
        if (TryGetProperty(root, "timestamp", out var timestamp) && timestamp.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(timestamp.GetString(), out var parsed)) return parsed;
        return DateTimeOffset.MinValue;
    }

    private static DateTime? ReadReset(JsonElement element)
    {
        foreach (var name in new[] { "resets_at", "resetsAt", "reset_at", "resetAt", "next_reset_at", "nextResetAt" })
        {
            if (!TryGetProperty(element, name, out var reset)) continue;
            try
            {
                if (reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var unix))
                    return (unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix)).LocalDateTime;
                if (reset.ValueKind == JsonValueKind.String)
                {
                    if (long.TryParse(reset.GetString(), out var unixText))
                        return (unixText > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unixText) : DateTimeOffset.FromUnixTimeSeconds(unixText)).LocalDateTime;
                    if (DateTimeOffset.TryParse(reset.GetString(), out var parsed)) return parsed.LocalDateTime;
                }
            }
            catch { }
        }
        return null;
    }
}

public sealed record ClaudeLiveResets(DateTime? FiveHourResetsAt, DateTime? WeeklyResetsAt);
