using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentCake;

/// <summary>
/// Reads Claude Code transcript logs (~/.claude/projects/**/*.jsonl) incrementally and rolls
/// them into a <see cref="UsageSnapshot"/>: tokens in the current 5-hour window and tokens
/// since the weekly reset. No Node/ccusage needed — it parses the same files ccusage does.
/// Not thread-safe; call <see cref="Scan"/> from a single background worker.
/// The block/reset math lives in <see cref="UsageMath"/> (unit-tested separately).
/// </summary>
public sealed class UsageReader
{
    private const int RetentionDays = 8;             // enough for a full weekly window + slack

    private readonly Func<AppSettings> _settings;
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new();
    private readonly List<(UsageRecord Rec, string? Key)> _records = new();

    public UsageReader(Func<AppSettings> settings) => _settings = settings;

    public string ResolveDataDir()
    {
        var s = _settings();
        if (!string.IsNullOrWhiteSpace(s.ClaudeDirOverride))
            return s.ClaudeDirOverride;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude");
    }

    public UsageSnapshot Scan()
    {
        var settings = _settings();
        string root = ResolveDataDir();
        string projects = Path.Combine(root, "projects");
        bool exists = Directory.Exists(projects);

        if (exists)
        {
            foreach (var file in EnumerateJsonl(projects))
            {
                try { ReadAppended(file); }
                catch { /* skip locked/unreadable file this pass */ }
            }
        }

        Prune();
        return Aggregate(settings, root, exists);
    }

    private static IEnumerable<string> EnumerateJsonl(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories); }
        catch { return Array.Empty<string>(); }
    }

    private void ReadAppended(string path)
    {
        long start = _offsets.TryGetValue(path, out var o) ? o : 0L;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < start) start = 0;          // truncated/rotated -> re-read

        if (fs.Length == start) { _offsets[path] = start; return; }

        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            ParseLine(line);
        }

        // Resume only on a clean line boundary so a half-written trailing line is re-read next pass.
        _offsets[path] = SafeOffset(fs);
    }

    private static long SafeOffset(FileStream fs)
    {
        long len = fs.Length;
        if (len == 0) return 0;
        try
        {
            fs.Seek(len - 1, SeekOrigin.Begin);
            if (fs.ReadByte() == '\n') return len;
            return LastNewlineOffset(fs, len);
        }
        catch { return len; }
    }

    private static long LastNewlineOffset(FileStream fs, long len)
    {
        const int chunk = 8192;
        long pos = len;
        var buf = new byte[chunk];
        while (pos > 0)
        {
            int read = (int)Math.Min(chunk, pos);
            pos -= read;
            fs.Seek(pos, SeekOrigin.Begin);
            int n = fs.Read(buf, 0, read);
            for (int i = n - 1; i >= 0; i--)
                if (buf[i] == (byte)'\n') return pos + i + 1;
        }
        return 0;
    }

    private void ParseLine(string line)
    {
        UsageRecord rec;
        string? key;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
            if (!msg.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;

            long input = GetLong(usage, "input_tokens");
            long output = GetLong(usage, "output_tokens");
            long cacheCreate = GetLong(usage, "cache_creation_input_tokens");
            long cacheRead = GetLong(usage, "cache_read_input_tokens");
            if (input == 0 && output == 0 && cacheCreate == 0 && cacheRead == 0) return;

            string model = msg.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString() ?? "" : "";

            if (!root.TryGetProperty("timestamp", out var tEl) || tEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(tEl.GetString(), out var dto))
                return;                              // can't time-bucket without a timestamp

            string? id = msg.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() : null;
            string? reqId = root.TryGetProperty("requestId", out var rEl) && rEl.ValueKind == JsonValueKind.String
                ? rEl.GetString()
                : (root.TryGetProperty("uuid", out var uEl) && uEl.ValueKind == JsonValueKind.String ? uEl.GetString() : null);
            key = id is null ? null : $"{id}|{reqId}";

            rec = new UsageRecord(dto.LocalDateTime, model, input, output, cacheCreate, cacheRead);
        }
        catch { return; }

        if (key is not null && !_seen.Add(key)) return;   // dedup (ccusage-style)
        _records.Add((rec, key));
    }

    private static long GetLong(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) ? v : 0L;

    private void Prune()
    {
        DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);
        int write = 0;
        for (int i = 0; i < _records.Count; i++)
        {
            var item = _records[i];
            if (item.Rec.TimestampLocal < cutoff)
            {
                if (item.Key is not null) _seen.Remove(item.Key);
            }
            else _records[write++] = item;
        }
        if (write < _records.Count)
            _records.RemoveRange(write, _records.Count - write);
    }

    private UsageSnapshot Aggregate(AppSettings settings, string root, bool exists)
    {
        DateTime now = DateTime.Now;
        DateTime weekStart = UsageMath.MostRecentReset(now, settings.WeeklyResetDay, settings.WeeklyResetHour);

        var week = new UsageTotals();
        foreach (var (rec, _) in _records)
            if (rec.TimestampLocal >= weekStart)
                week.Add(rec);

        var window = new UsageTotals();
        DateTime? windowEnd = null;
        var block = UsageMath.LastBlock(_records.Select(r => r.Rec.TimestampLocal));
        if (block is { } b && now < b.EndsAt)
        {
            foreach (var (rec, _) in _records)
                if (rec.TimestampLocal >= b.FirstTs)
                    window.Add(rec);
            windowEnd = b.EndsAt;
        }

        return new UsageSnapshot
        {
            CurrentWindow = window,
            Week = week,
            WindowEndsAt = windowEnd,
            WeekStartedAt = weekStart,
            WeekResetsAt = weekStart.AddDays(7),
            GeneratedAt = now,
            DataDir = root,
            DataDirExists = exists
        };
    }
}
