using System.Text;
using System.Text.Json;

namespace AgentCake;

public sealed class UsageReader
{
    private readonly Func<AppSettings> _settings;

    public UsageReader(Func<AppSettings> settings) => _settings = settings;

    public UsageSnapshot Scan()
    {
        var cfg = _settings();
        return new UsageSnapshot(ReadCodex(cfg.ResolveCodexSessionsDir()), ReadClaudeDesktop(cfg.ResolveClaudeDesktopUsagePath()), DateTime.Now);
    }

    private static ServiceUsage ReadCodex(string sessionsDir)
    {
        if (!Directory.Exists(sessionsDir))
            return ServiceUsage.Unavailable("Codex", "Codex session folder was not found.");

        try
        {
            var files = Directory.EnumerateFiles(sessionsDir, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(12);

            foreach (var file in files)
            {
                ServiceUsage? latest = null;
                foreach (var line in TailLines(file.FullName))
                    if (UsageParsers.TryParseCodexWeekly(line, out var usage)) latest = usage;
                if (latest is not null) return latest;
            }
        }
        catch { }

        return ServiceUsage.Unavailable("Codex", "No live weekly rate-limit record has been written yet.");
    }

    private static ServiceUsage ReadClaudeDesktop(string historyPath)
    {
        if (!File.Exists(historyPath))
            return ServiceUsage.Unavailable("Claude", "Claude Desktop plan-usage history was not found. Open Claude Desktop and sign in.");

        try
        {
            return UsageParsers.TryParseClaudeDesktopWeekly(File.ReadAllText(historyPath), out var usage)
                ? usage
                : ServiceUsage.Unavailable("Claude", "Claude Desktop has not recorded a weekly usage value yet.");
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
}

public static class UsageParsers
{
    public static bool TryParseCodexWeekly(string json, out ServiceUsage usage)
    {
        usage = ServiceUsage.Unavailable("Codex", "No live weekly limit.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var limits = FindNamedObject(doc.RootElement, "rate_limits");
            if (limits is null) return false;

            var candidates = new List<(JsonElement Window, double Minutes)>();
            foreach (var name in new[] { "primary", "secondary", "weekly", "seven_day" })
            {
                if (limits.Value.TryGetProperty(name, out var window) && window.ValueKind == JsonValueKind.Object && TryNumber(window, "used_percent", out _))
                {
                    _ = TryNumber(window, "window_minutes", out var minutes);
                    candidates.Add((window, minutes));
                }
            }
            if (candidates.Count == 0) return false;

            var weekly = candidates.OrderByDescending(candidate => candidate.Minutes).First();
            if (!TryNumber(weekly.Window, "used_percent", out var used)) return false;
            TimeSpan? weeklyWindow = weekly.Minutes > 0 ? TimeSpan.FromMinutes(weekly.Minutes) : null;
            usage = new ServiceUsage("Codex", used, ReadReset(weekly.Window), "Live Codex account limit", WeeklyWindow: weeklyWindow);
            return true;
        }
        catch { return false; }
    }

    public static bool TryParseClaudeDesktopWeekly(string json, out ServiceUsage usage)
    {
        usage = ServiceUsage.Unavailable("Claude", "No Claude Desktop weekly limit.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
                return false;

            var weeklyResetsAt = ReadClaudeDesktopReset(samples, "sd", TimeSpan.FromDays(7));
            var fiveHourResetsAt = ReadClaudeDesktopReset(samples, "fh", TimeSpan.FromHours(5));

            for (var index = samples.GetArrayLength() - 1; index >= 0; index--)
            {
                var sample = samples[index];
                if (sample.TryGetProperty("u", out var usageValues) && usageValues.ValueKind == JsonValueKind.Object
                    && TryNumber(usageValues, "sd", out var used))
                {
                    double? fiveHourUsed = TryNumber(usageValues, "fh", out var parsedFiveHourUsed)
                        ? parsedFiveHourUsed
                        : null;
                    usage = new ServiceUsage("Claude", used, weeklyResetsAt, weeklyResetsAt is null
                        ? "Live Claude Desktop plan usage"
                        : "Live Claude Desktop plan usage; reset times are based on observed usage resets.",
                        fiveHourUsed,
                        fiveHourResetsAt,
                        WeeklyWindow: TimeSpan.FromDays(7));
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }

    private static DateTime? ReadClaudeDesktopReset(JsonElement samples, string usageKey, TimeSpan window)
    {
        double? previousUsed = null;
        long? previousTimestamp = null;
        DateTime? latestReset = null;

        foreach (var sample in samples.EnumerateArray())
        {
            if (!sample.TryGetProperty("u", out var usageValues) || usageValues.ValueKind != JsonValueKind.Object
                || !TryNumber(usageValues, usageKey, out var used)
                || !TryNumber(sample, "t", out var timestamp))
                continue;

            // Claude Desktop stores sampled usage but no reset timestamp. A large
            // drop to near-zero marks the reset window for this specific allowance.
            if (previousUsed is { } previous && previous >= 50 && used <= 5 && used < previous)
            {
                try
                {
                    // The reset happened between two five-minute history samples. The
                    // midpoint avoids presenting the polling offset as the reset time.
                    long resetTimestamp = previousTimestamp is { } previousTime
                        ? previousTime + ((long)timestamp - previousTime) / 2
                        : (long)timestamp;
                    latestReset = RoundToNearestMinute(DateTimeOffset.FromUnixTimeMilliseconds(resetTimestamp).LocalDateTime);
                }
                catch { }
            }

            previousUsed = used;
            previousTimestamp = (long)timestamp;
        }

        return latestReset?.Add(window);
    }

    private static DateTime RoundToNearestMinute(DateTime value)
    {
        value = value.AddSeconds(30);
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));
    }

    private static JsonElement? FindNamedObject(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out var direct) && direct.ValueKind == JsonValueKind.Object) return direct.Clone();
            foreach (var property in element.EnumerateObject())
            {
                var found = FindNamedObject(property.Value, name);
                if (found is not null) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindNamedObject(item, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static bool TryNumber(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value);
    }

    private static DateTime? ReadReset(JsonElement element)
    {
        if (!element.TryGetProperty("resets_at", out var reset)) return null;
        try
        {
            if (reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var unix))
                return (unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix)).LocalDateTime;
            if (reset.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(reset.GetString(), out var parsed))
                return parsed.LocalDateTime;
        }
        catch { }
        return null;
    }
}
