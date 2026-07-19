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
        return new UsageSnapshot(ReadCodex(cfg.ResolveCodexSessionsDir()), ReadClaude(cfg.ResolveClaudeStatusPath()), DateTime.Now);
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

    private static ServiceUsage ReadClaude(string statusPath)
    {
        if (!File.Exists(statusPath))
            return ServiceUsage.Unavailable("Claude", "No Claude status payload yet. Run Install-ClaudeStatusHook.ps1, then use Claude Code once.");

        try
        {
            return UsageParsers.TryParseClaudeWeekly(File.ReadAllText(statusPath), out var usage)
                ? usage
                : ServiceUsage.Unavailable("Claude", "The latest Claude status payload has no weekly limit.");
        }
        catch
        {
            return ServiceUsage.Unavailable("Claude", "Claude status payload is being updated; retrying shortly.");
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

            var weekly = candidates.OrderByDescending(candidate => candidate.Minutes).First().Window;
            if (!TryNumber(weekly, "used_percent", out var used)) return false;
            usage = new ServiceUsage("Codex", used, ReadReset(weekly), "Live Codex account limit");
            return true;
        }
        catch { return false; }
    }

    public static bool TryParseClaudeWeekly(string json, out ServiceUsage usage)
    {
        usage = ServiceUsage.Unavailable("Claude", "No live weekly limit.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var limits = FindNamedObject(doc.RootElement, "rate_limits") ?? FindNamedObject(doc.RootElement, "rate_limit");
            if (limits is null) return false;

            foreach (var name in new[] { "seven_day", "weekly", "secondary" })
            {
                if (limits.Value.TryGetProperty(name, out var weekly) && weekly.ValueKind == JsonValueKind.Object
                    && (TryNumber(weekly, "used_percentage", out var used) || TryNumber(weekly, "used_percent", out used)))
                {
                    usage = new ServiceUsage("Claude", used, ReadReset(weekly), "Live Claude Code status limit");
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
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