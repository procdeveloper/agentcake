using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentCake;

public enum PlanTier
{
    Pro,    // ~44k tokens / 5h window
    Max5,   // ~88k tokens / 5h window
    Max20,  // ~220k tokens / 5h window
    Custom
}

public sealed class AppSettings
{
    public PlanTier Plan { get; set; } = PlanTier.Max5;

    /// <summary>Used only when Plan == Custom.</summary>
    public int Custom5hCapTokens { get; set; } = 88_000;

    /// <summary>
    /// Weekly token budget. Anthropic does NOT publish the weekly limit as a token number
    /// (it is hours-based and opaque), so this is a budget you set/calibrate. Default is a
    /// rough placeholder = 40x the 5h cap; tune it by watching when Claude actually warns you.
    /// 0 or negative => auto (40x the 5h cap).
    /// </summary>
    public int WeeklyCapTokens { get; set; } = 0;

    /// <summary>Weekday the weekly limit resets on (local time).</summary>
    public DayOfWeek WeeklyResetDay { get; set; } = DayOfWeek.Monday;

    /// <summary>Hour of day (0-23, local) the weekly limit resets.</summary>
    public int WeeklyResetHour { get; set; } = 0;

    public int RefreshSeconds { get; set; } = 15;

    /// <summary>If true, cache-read tokens count toward the gauges. They are limited far more
    /// cheaply, so leaving this off is usually closer to reality.</summary>
    public bool CountCacheReadsInWindow { get; set; } = false;

    /// <summary>Optional path to the Claude data dir. Empty = auto (%USERPROFILE%\.claude).</summary>
    public string ClaudeDirOverride { get; set; } = "";

    // ---- derived ----

    [JsonIgnore]
    public int Window5hCapTokens => Plan switch
    {
        PlanTier.Pro => 44_000,
        PlanTier.Max5 => 88_000,
        PlanTier.Max20 => 220_000,
        _ => Math.Max(1_000, Custom5hCapTokens)
    };

    [JsonIgnore]
    public int EffectiveWeeklyCapTokens =>
        WeeklyCapTokens > 0 ? WeeklyCapTokens : Window5hCapTokens * 40;

    // ---- persistence ----

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCake");

    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), JsonOpts);
                if (s is not null) return s;
            }
        }
        catch { /* defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* non-fatal */ }
    }
}
