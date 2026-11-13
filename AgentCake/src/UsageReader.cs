using System.Text;
using System.Text.Json;

namespace AgentCake;

public sealed class UsageReader
{
    private readonly Func<AppSettings> _settings;
    private readonly string? _cachePath;
    private readonly string? _claudeCodeStatusPath;
    private readonly IReadOnlyList<string>? _claudeDataDirs;
    private ServiceUsage? _lastCodexUsage;
    private ServiceUsage? _lastClaudeUsage;
    private string? _loadedSourceKey;

    public UsageReader(Func<AppSettings> settings) : this(settings, null) { }

    internal UsageReader(Func<AppSettings> settings, string? cachePath, string? claudeCodeStatusPath = null, IEnumerable<string>? claudeDataDirs = null)
    {
        _settings = settings;
        _cachePath = cachePath;
        _claudeCodeStatusPath = claudeCodeStatusPath;
        _claudeDataDirs = claudeDataDirs?.ToList();
    }

    public UsageSnapshot Scan()
    {
        var cfg = _settings();
        string codexPath = cfg.ResolveCodexSessionsDir();
        string claudeHistoryPath = cfg.ResolveClaudeDesktopUsagePath();
        string claudeLogPath = cfg.ResolveClaudeDesktopLogPath();
        string sourceKey = $"{Path.GetFullPath(codexPath)}|{Path.GetFullPath(claudeHistoryPath)}";
        EnsureCacheLoaded(sourceKey);

        var codex = ReadCodex(codexPath);
        var (desktop, desktopSampledAt) = ReadClaudeDesktop(claudeHistoryPath, claudeLogPath);
        var (code, codeSampledAt) = ReadClaudeCodeStatus(_claudeCodeStatusPath ?? AppSettings.ClaudeCodeStatusPath);
        var cached = ClaudeCacheReader.ReadNewest(_claudeDataDirs ?? AppSettings.ClaudeDesktopDataDirs());

        // Claude Desktop, its cached usage responses and Claude Code all describe
        // the same account allowance. Prefer whichever observed it most recently.
        ServiceUsage claude = desktop;
        DateTimeOffset claudeSampledAt = desktop.UsedPercent is null ? DateTimeOffset.MinValue : desktopSampledAt;
        if (cached is not null && cached.FetchedAt > claudeSampledAt)
        {
            claude = cached.Usage with
            {
                Service = "Claude",
                Detail = $"Claude usage as of {cached.FetchedAt.LocalDateTime:HH:mm}, from Claude Desktop's cached usage response."
            };
            claudeSampledAt = cached.FetchedAt;
        }
        if (code is not null && codeSampledAt > claudeSampledAt)
            claude = code with { Service = "Claude" };

        if (claude.UsedPercent is not null)
            _lastClaudeUsage = claude;
        else if (_lastClaudeUsage is not null)
            claude = _lastClaudeUsage with { Detail = $"Last good Claude reading retained because refresh failed: {claude.Detail}" };

        var claudeCode = claude.UsedPercent is null
            ? ServiceUsage.Unavailable("Claude Code", "No Claude account usage has been recorded yet.")
            : claude with { Service = "Claude Code" };

        var snapshot = new UsageSnapshot(codex, claude, claudeCode, DateTime.Now);
        SaveCache(sourceKey);
        return snapshot;
    }

    private ServiceUsage ReadCodex(string sessionsDir)
    {
        const string missing = "No live weekly account-limit record has been written yet.";
        var roots = ResolveCodexSessionRoots(sessionsDir).ToList();
        if (roots.Count == 0)
            return _lastCodexUsage ?? ServiceUsage.Unavailable("Codex", "Codex session folder was not found.");

        try
        {
            var files = roots.SelectMany(root => Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc);

            var records = new List<CodexUsageRecord>();
            int scanned = 0;
            foreach (var file in files)
            {
                scanned++;
                try
                {
                    // Never fall back to reading an entire session. A single JSONL
                    // file can be hundreds of megabytes, which used to pin the
                    // refresh worker for minutes and made Refresh appear broken.
                    records.AddRange(FindCodexRecords(TailLines(file.FullName)));
                }
                catch (FileNotFoundException) { }
                catch (Exception exception)
                {
                    CrashLog.Write($"Codex session tail could not be read: {file.FullName}", exception);
                }

                if (scanned >= 160) break;
            }

            var codex = RestorePaceFromPrevious(BuildCodexUsage(records.Where(IsCodexMainLimit)), _lastCodexUsage);
            if (codex is not null) _lastCodexUsage = codex;
            return codex ?? _lastCodexUsage ?? ServiceUsage.Unavailable("Codex", missing);
        }
        catch (Exception exception) { CrashLog.Write("Codex usage scan failed", exception); }

        return _lastCodexUsage ?? ServiceUsage.Unavailable("Codex", missing);
    }

    private static IEnumerable<string> ResolveCodexSessionRoots(string sessionsDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string absolute = Path.GetFullPath(sessionsDir);
        if (Directory.Exists(absolute) && seen.Add(absolute)) yield return absolute;

        // Codex moves completed conversations out of sessions, so the newest
        // authoritative limit can live there when Codex has not run recently.
        if (string.Equals(Path.GetFileName(absolute.TrimEnd(Path.DirectorySeparatorChar)), "sessions", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(absolute)?.FullName;
            if (parent is not null)
            {
                string archived = Path.Combine(parent, "archived_sessions");
                if (Directory.Exists(archived) && seen.Add(archived)) yield return archived;
            }
        }
    }

    private static ServiceUsage? RestorePaceFromPrevious(ServiceUsage? current, ServiceUsage? previous)
    {
        if (current is null || previous is null || current.BurnPaceRatio is not null)
            return current;

        if (previous.BurnPaceRatio is null)
            return current;

        if (!ResetsInSameWindow(current.ResetsAt, previous.ResetsAt))
            return current;

        // Keep a recently known throttle ratio when we only have one fresh
        // sample and no history path to recompute it immediately.
        return current with
        {
            BurnRatePercentPerHour = previous.BurnRatePercentPerHour,
            BurnPaceRatio = previous.BurnPaceRatio
        };
    }

    private static bool ResetsInSameWindow(DateTime? left, DateTime? right)
        => left == right
            || (left is not null && right is not null && Math.Abs((left.Value - right.Value).TotalSeconds) <= 90);

    private static ServiceUsage? BuildCodexUsage(IEnumerable<CodexUsageRecord> source)
    {
        var records = source.ToList();
        var newest = records.OrderByDescending(record => record.RecordedAt).FirstOrDefault();
        if (newest is null) return null;
        var pace = UsagePace.Estimate(records.Select(record => new UsageSample(record.RecordedAt, record.Usage.UsedPercent ?? 0, record.Usage.ResetsAt)), newest.Usage);
        return newest.Usage with { BurnRatePercentPerHour = pace?.BurnRatePercentPerHour, BurnPaceRatio = pace?.BurnPaceRatio };
    }

    // Codex also reports separate per-model allowances (e.g. Spark); only the
    // shared account limit is shown.
    private static bool IsCodexMainLimit(CodexUsageRecord record)
        => !UsageParsers.IsSecondaryCodexLimit(record.LimitId, record.LimitName);

    private static (ServiceUsage Usage, DateTimeOffset SampledAt) ReadClaudeDesktop(string historyPath, string logPath)
    {
        if (!File.Exists(historyPath))
            return (ServiceUsage.Unavailable("Claude", "Claude Desktop plan-usage history was not found. Open Claude Desktop and sign in."), DateTimeOffset.MinValue);

        try
        {
            string historyJson = ReadSharedText(historyPath);

            // The percentage history is the primary source. Parse it before the
            // optional reset log so a rotated/locked log can never erase usage.
            DateTime? observedWeeklyReset = UsageParsers.ReadClaudeDesktopObservedWeeklyReset(historyJson);
            if (!UsageParsers.TryParseClaudeDesktopWeekly(historyJson, out var usage, observedWeeklyReset))
                return (ServiceUsage.Unavailable("Claude", "Claude Desktop has not recorded a weekly usage value yet."), DateTimeOffset.MinValue);
            DateTimeOffset sampledAt = UsageParsers.ReadClaudeDesktopLatestSampleTime(historyJson);

            ClaudeLiveResets? realResets = null;
            try { realResets = ResolveClaudeDesktopLiveResets(logPath); }
            catch (Exception exception) { CrashLog.Write("Claude reset-log scan failed; usage percentage was preserved", exception); }

            DateTime? fiveHourReset = realResets?.FiveHourResetsAt > DateTime.Now ? realResets.FiveHourResetsAt : null;
            DateTime? liveWeeklyReset = realResets?.WeeklyResetsAt > DateTime.Now ? realResets.WeeklyResetsAt : null;
            DateTime? weeklyReset = liveWeeklyReset ?? observedWeeklyReset;

            if (weeklyReset != observedWeeklyReset
                && UsageParsers.TryParseClaudeDesktopWeekly(historyJson, out var usageWithLiveReset, weeklyReset))
                usage = usageWithLiveReset;

            return (usage with
            {
                ResetsAt = weeklyReset,
                FiveHourResetsAt = fiveHourReset,
                Detail = liveWeeklyReset is not null
                    ? "Live Claude Desktop plan usage; weekly reset time comes from Claude Desktop's live rate-limit response."
                    : observedWeeklyReset is not null
                        ? "Live Claude Desktop plan usage; weekly reset was observed in Claude Desktop's own usage history."
                        : "Live Claude Desktop plan usage; reset time is unavailable until Claude writes a live rate-limit response."
            }, sampledAt);
        }
        catch (Exception exception)
        {
            CrashLog.Write("Claude usage-history scan failed", exception);
            return (ServiceUsage.Unavailable("Claude", "Claude Desktop usage history is being updated; retrying shortly."), DateTimeOffset.MinValue);
        }
    }

    private static (ServiceUsage? Usage, DateTimeOffset SampledAt) ReadClaudeCodeStatus(string statusPath)
    {
        try
        {
            if (!File.Exists(statusPath)) return (null, DateTimeOffset.MinValue);
            var sampledAt = new DateTimeOffset(File.GetLastWriteTimeUtc(statusPath));
            return UsageParsers.TryParseClaudeCodeStatus(ReadSharedText(statusPath), out var usage)
                ? (usage, sampledAt)
                : (null, DateTimeOffset.MinValue);
        }
        catch (Exception exception)
        {
            CrashLog.Write("Claude Code status payload could not be read", exception);
            return (null, DateTimeOffset.MinValue);
        }
    }

    private static ClaudeLiveResets? ResolveClaudeDesktopLiveResets(string configuredLogPath)
    {
        var candidates = ResolveClaudeDesktopLogCandidates(configuredLogPath).ToList();
        foreach (var candidatePath in candidates)
        {
            if (!File.Exists(candidatePath)) continue;
            try
            {
                var liveResets = UsageParsers.ReadClaudeDesktopLiveResets(TailLines(candidatePath));
                if (liveResets is not null) return liveResets;
            }
            catch (Exception exception)
            {
                CrashLog.Write($"Claude reset log could not be read: {candidatePath}", exception);
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveClaudeDesktopLogCandidates(string configuredLogPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(configuredLogPath))
        {
            var absolute = Path.GetFullPath(configuredLogPath);
            if (seen.Add(absolute))
                yield return absolute;
        }

        var directory = Path.GetDirectoryName(configuredLogPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "logs");

        if (!Directory.Exists(directory)) yield break;

        var orderedLogFiles = Directory.EnumerateFiles(directory, "claude.ai-web*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var file in orderedLogFiles)
        {
            string absolute = file.FullName;
            if (seen.Add(absolute))
                yield return absolute;
        }
    }

    private static IEnumerable<string> TailLines(string path)
    {
        const int maxBytes = 512 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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

    private static string ReadSharedText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private void EnsureCacheLoaded(string sourceKey)
    {
        if (_loadedSourceKey == sourceKey) return;
        _loadedSourceKey = sourceKey;
        _lastCodexUsage = null;
        _lastClaudeUsage = null;
        if (_cachePath is null || !File.Exists(_cachePath)) return;

        try
        {
            var cache = JsonSerializer.Deserialize<UsageCache>(ReadSharedText(_cachePath));
            if (cache?.SourceKey != sourceKey) return;
            _lastCodexUsage = cache.Codex?.UsedPercent is not null ? cache.Codex : null;
            _lastClaudeUsage = cache.Claude?.UsedPercent is not null ? cache.Claude : null;
        }
        catch (Exception exception) { CrashLog.Write("Last-good usage cache could not be loaded", exception); }
    }

    private void SaveCache(string sourceKey)
    {
        if (_cachePath is null) return;
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (directory is not null) Directory.CreateDirectory(directory);
            string tempPath = _cachePath + ".tmp";
            string json = JsonSerializer.Serialize(new UsageCache(sourceKey, _lastCodexUsage, _lastClaudeUsage), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, _cachePath, true);
        }
        catch (Exception exception) { CrashLog.Write("Last-good usage cache could not be saved", exception); }
    }

    private static IEnumerable<CodexUsageRecord> FindCodexRecords(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!line.Contains("\"rate_limits\"", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("\"rateLimits\"", StringComparison.OrdinalIgnoreCase)) continue;
            if (!UsageParsers.TryParseCodexLimits(line, out var usages, out var recordedAt)) continue;
            foreach (var usage in usages) yield return new CodexUsageRecord(usage.LimitId, usage.LimitName, usage.Usage, recordedAt);
        }
    }
}

internal sealed record UsageCache(string SourceKey, ServiceUsage? Codex, ServiceUsage? Claude);

internal sealed record CodexUsageRecord(string LimitId, string? LimitName, ServiceUsage Usage, DateTimeOffset RecordedAt);
internal sealed record CodexLimitUsage(string LimitId, string? LimitName, ServiceUsage Usage);

public static class UsageParsers
{
    // Per-model side allowances (Spark / "bengalfox") that are not the shared
    // Codex account limit.
    internal static bool IsSecondaryCodexLimit(string limitId, string? limitName)
        => limitId.Contains("spark", StringComparison.OrdinalIgnoreCase)
            || limitId.Contains("bengalfox", StringComparison.OrdinalIgnoreCase)
            || limitName?.Contains("Spark", StringComparison.OrdinalIgnoreCase) == true
            || limitName?.Contains("Bengal", StringComparison.OrdinalIgnoreCase) == true;

    private static string ResolveCodexServiceName(string limitId, string? limitName)
        => IsSecondaryCodexLimit(limitId, limitName)
            ? $"Codex {limitName ?? limitId}"
            : "Codex";

    // Claude's usage endpoints (api.anthropic.com/api/oauth/usage and
    // claude.ai/api/organizations/{org}/usage) report utilization as 0-100.
    public static bool TryParseClaudeUsageResponse(byte[] json, out ServiceUsage usage)
    {
        usage = ServiceUsage.Unavailable("Claude", "No Claude usage in response.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("seven_day", out var weekly) || weekly.ValueKind != JsonValueKind.Object
                || !TryNumber(weekly, "utilization", out var weeklyUsed) || weeklyUsed is < 0 or > 100)
                return false;

            double? fiveHourUsed = null;
            DateTime? fiveHourReset = null;
            if (root.TryGetProperty("five_hour", out var fiveHour) && fiveHour.ValueKind == JsonValueKind.Object
                && TryNumber(fiveHour, "utilization", out var parsedFiveHour) && parsedFiveHour is >= 0 and <= 100)
            {
                fiveHourUsed = parsedFiveHour;
                fiveHourReset = ReadReset(fiveHour);
            }

            usage = new ServiceUsage("Claude", weeklyUsed, ReadReset(weekly), "Claude usage response",
                fiveHourUsed, fiveHourReset, WeeklyWindow: TimeSpan.FromDays(7));
            return true;
        }
        catch { return false; }
    }

    // Claude Code passes its statusline command a JSON payload whose
    // rate_limits block carries the account's live 5-hour and 7-day usage.
    public static bool TryParseClaudeCodeStatus(string json, out ServiceUsage usage)
    {
        usage = ServiceUsage.Unavailable("Claude Code", "No live Claude Code rate-limit data.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!TryGetProperty(doc.RootElement, "rate_limits", out var limits) && !TryGetProperty(doc.RootElement, "rateLimits", out limits))
                return false;
            if (limits.ValueKind != JsonValueKind.Object) return false;

            bool hasWeekly = TryGetProperty(limits, "seven_day", out var weekly) || TryGetProperty(limits, "sevenDay", out weekly) || TryGetProperty(limits, "7d", out weekly);
            bool hasFiveHour = TryGetProperty(limits, "five_hour", out var fiveHour) || TryGetProperty(limits, "fiveHour", out fiveHour) || TryGetProperty(limits, "5h", out fiveHour);
            if (!hasWeekly || weekly.ValueKind != JsonValueKind.Object || !TryReadStatusPercent(weekly, out var weeklyUsed))
                return false;

            double? fiveHourUsed = null;
            DateTime? fiveHourReset = null;
            if (hasFiveHour && fiveHour.ValueKind == JsonValueKind.Object && TryReadStatusPercent(fiveHour, out var parsedFiveHour))
            {
                fiveHourUsed = parsedFiveHour;
                fiveHourReset = ReadReset(fiveHour);
            }

            usage = new ServiceUsage("Claude Code", weeklyUsed, ReadReset(weekly),
                "Live Claude account usage reported by Claude Code.",
                fiveHourUsed, fiveHourReset, WeeklyWindow: TimeSpan.FromDays(7));
            return true;
        }
        catch { return false; }
    }

    private static bool TryReadStatusPercent(JsonElement window, out double used)
    {
        foreach (var name in new[] { "used_percentage", "usedPercentage" })
        {
            if (TryNumber(window, name, out used) && used is >= 0 and <= 100) return true;
        }
        if (TryReadUsedPercent(window, out used)) return true;
        if (TryNumber(window, "utilization", out var utilization) && utilization is >= 0 and <= 1.5)
        {
            used = Math.Min(100, utilization * 100);
            return true;
        }
        used = 0;
        return false;
    }

    public static DateTimeOffset ReadClaudeDesktopLatestSampleTime(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
                return DateTimeOffset.MinValue;
            double latest = 0;
            foreach (var sample in samples.EnumerateArray())
                if (TryNumber(sample, "t", out var t) && t > latest) latest = t;
            return latest > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)latest) : DateTimeOffset.MinValue;
        }
        catch { return DateTimeOffset.MinValue; }
    }

    public static bool TryParseCodexWeekly(string json, out ServiceUsage usage)
        => TryParseCodexWeekly(json, out usage, out _);

    internal static bool TryParseCodexWeekly(string json, out ServiceUsage usage, out DateTimeOffset recordedAt)
    {
        if (!TryParseCodexLimits(json, out var usages, out recordedAt))
        {
            usage = ServiceUsage.Unavailable("Codex", "No live Codex limit.");
            return false;
        }

        var selected = usages.FirstOrDefault(candidate =>
            string.Equals(candidate.LimitId, "codex", StringComparison.OrdinalIgnoreCase));

        usage = selected?.Usage
            ?? ServiceUsage.Unavailable("Codex", "No live Codex limit.");
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
                string service = ResolveCodexServiceName(limitId, limitName);
                TimeSpan? weeklyWindow = weekly.Minutes > 0 ? TimeSpan.FromMinutes(weekly.Minutes) : null;
                parsed.Add(new CodexLimitUsage(limitId, limitName, new ServiceUsage(service, used, ReadReset(weekly.Window), "Live Codex model allowance", WeeklyWindow: weeklyWindow)));
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
            foreach (var payload in ExtractJsonPayloads(line))
            {
                if (!TryReadClaudeLiveResets(payload, out var fiveHour, out var weekly)) continue;
                if (fiveHour is not null || weekly is not null) return new ClaudeLiveResets(fiveHour, weekly);
            }
        }
        return null;
    }

    private static IEnumerable<JsonElement> ExtractJsonPayloads(string line)
    {
        foreach (var payload in ExtractJsonFragments(line))
        {
            if (!TryParseJson(payload, out var doc))
                continue;

            using (doc)
            {
                yield return doc.RootElement.Clone();
            }
        }
    }

    private static IEnumerable<string> ExtractJsonFragments(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        for (int start = 0; start < text.Length; start++)
        {
            if (text[start] != '{') continue;
            bool inString = false;
            bool escaped = false;
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped) { escaped = false; continue; }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[start..(i + 1)];
                        break;
                    }
                }
            }
        }
    }

    private static bool TryReadClaudeLiveResets(JsonElement element, out DateTime? fiveHourResetsAt, out DateTime? weeklyResetsAt)
    {
        fiveHourResetsAt = null;
        weeklyResetsAt = null;

        if (element.ValueKind == JsonValueKind.Object)
        {
            bool isFiveHour = IsFiveHourRepresentative(element);
            if (ReadReset(element) is { } topLevelReset)
            {
                if (isFiveHour)
                    fiveHourResetsAt = topLevelReset;
                else if (weeklyResetsAt is null)
                    weeklyResetsAt = topLevelReset;
            }

            if (element.TryGetProperty("windows", out var windows) && windows.ValueKind == JsonValueKind.Object)
            {
                fiveHourResetsAt = windows.TryGetProperty("5h", out var fiveHourWindow) ? ReadReset(fiveHourWindow) : null;
                weeklyResetsAt = windows.TryGetProperty("7d", out var weeklyWindow) ? ReadReset(weeklyWindow) : null;
                if (fiveHourResetsAt is not null || weeklyResetsAt is not null) return true;
            }

            bool hasRepresentativeClaim = TryString(element, "representativeClaim", out var representativeClaim)
                && !string.IsNullOrWhiteSpace(representativeClaim);
            if (TryReadResolvedReset(element, out var resolvedReset))
            {
                // Claude's resolved limit is the blocking 5h/session limit when
                // representativeClaim is omitted. An explicit non-5h claim is
                // the only safe signal that this reset belongs to the week.
                if (isFiveHour || !hasRepresentativeClaim)
                    fiveHourResetsAt ??= resolvedReset;
                else
                    weeklyResetsAt ??= resolvedReset;
            }

            if (fiveHourResetsAt is not null || weeklyResetsAt is not null) return true;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    if (TryReadClaudeLiveResets(property.Value, out fiveHourResetsAt, out weeklyResetsAt))
                        return true;
                }
                else if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    if (TryReadClaudeLiveResets(property.Value, out fiveHourResetsAt, out weeklyResetsAt))
                        return true;
                }
                else if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is string nestedText
                    && ContainsJsonText(nestedText))
                {
                    if (TryReadClaudeLiveResetsFromText(nestedText, out fiveHourResetsAt, out weeklyResetsAt))
                        return true;
                }
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryReadClaudeLiveResets(item, out fiveHourResetsAt, out weeklyResetsAt))
                    return true;
            }
        }

        return false;
    }

    private static bool IsFiveHourRepresentative(JsonElement element)
    {
        if (!TryString(element, "representativeClaim", out var representativeClaim) || representativeClaim is null)
            return false;
        return representativeClaim.Contains("five", StringComparison.OrdinalIgnoreCase)
            || representativeClaim.Contains("5h", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadResolvedReset(JsonElement element, out DateTime? reset)
    {
        if (element.TryGetProperty("resolved", out var resolved) && resolved.ValueKind == JsonValueKind.Object
            && resolved.TryGetProperty("limit", out var limit) && limit.ValueKind == JsonValueKind.Object
            && ReadReset(limit) is { } resolvedReset)
        {
            reset = resolvedReset;
            return true;
        }

        reset = null;
        return false;
    }

    private static bool TryReadClaudeLiveResetsFromText(string text, out DateTime? fiveHourResetsAt, out DateTime? weeklyResetsAt)
    {
        fiveHourResetsAt = null;
        weeklyResetsAt = null;

        if (!ContainsJsonText(text)) return false;

        foreach (var payload in ExtractJsonFragments(text))
        {
            if (!TryParseJson(payload, out var doc)) continue;
            using var parsed = doc;
            if (TryReadClaudeLiveResets(parsed.RootElement, out fiveHourResetsAt, out weeklyResetsAt))
                return true;
        }

        return false;
    }

    private static bool TryParseJson(string payload, out JsonDocument doc)
    {
        doc = null!;
        try
        {
            doc = JsonDocument.Parse(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsJsonText(string text)
        => text.Contains('{') && text.Contains('}') && text.Contains('"');

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
