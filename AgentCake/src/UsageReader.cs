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
                // A plan change can start a fresh session before it has emitted a
                // limit event. Search a useful history instead of treating the
                // newest dozen session files as the complete account history.
                .Take(80);

            foreach (var file in files)
            {
                ServiceUsage? latest = null;
                foreach (var line in TailLines(file.FullName))
                    if (UsageParsers.TryParseCodexWeekly(line, out var usage)) latest = usage;
                if (latest is not null) return latest;

                // Session JSONL records can contain a very large prompt or tool
                // result. If the rate-limit record lives near the start of one of
                // those lines, a byte-tail begins mid-JSON and cannot parse it.
                // Only then fall back to a complete, shared-read scan of this file.
                foreach (var line in AllLines(file.FullName))
                    if (line.Contains("\"rate_limits\"", StringComparison.Ordinal) && UsageParsers.TryParseCodexWeekly(line, out var fullUsage)) latest = fullUsage;
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

    private static IEnumerable<string> AllLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line)) yield return line;
        }
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
            var limitSets = FindNamedObjects(doc.RootElement, "rate_limits", "rateLimits", "rate_limit", "rateLimit", "limits").ToList();
            if (limitSets.Count == 0) return false;

            var candidates = new List<(JsonElement Window, double Minutes, int NameScore)>();
            foreach (var limits in limitSets)
            {
                foreach (var property in limits.EnumerateObject())
                {
                    var window = property.Value;
                    if (window.ValueKind != JsonValueKind.Object || !TryReadUsedPercent(window, out _)) continue;

                    candidates.Add((window, ReadWindowMinutes(window), WindowNameScore(property.Name)));
                }
            }
            if (candidates.Count == 0) return false;

            // Codex identifies these windows differently across plans. A real
            // duration is authoritative; the name is only a tie-breaker.
            var weekly = candidates
                .OrderByDescending(candidate => candidate.Minutes)
                .ThenByDescending(candidate => candidate.NameScore)
                .First();
            if (!TryReadUsedPercent(weekly.Window, out var used)) return false;
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
