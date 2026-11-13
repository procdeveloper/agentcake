using System.Text.Json;

namespace AgentCake;

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 30;
    public bool ShowUsageBarsInTray { get; set; } = false;
    public bool StayOnTop { get; set; } = false;
    public ProviderSettings Providers { get; set; } = new();
    public string CodexSessionsDir { get; set; } = "";
    public string ClaudeDesktopUsagePath { get; set; } = "";
    public string ClaudeDesktopLogPath { get; set; } = "";

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCake");
    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");
    public static string UsageCachePath => Path.Combine(ConfigDir, "last-good-usage.json");

    public string ResolveCodexSessionsDir() => string.IsNullOrWhiteSpace(CodexSessionsDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions")
        : CodexSessionsDir;

    public string ResolveClaudeDesktopUsagePath() => string.IsNullOrWhiteSpace(ClaudeDesktopUsagePath)
        ? NewestExisting(ClaudeDesktopDataDirs().Select(dir => Path.Combine(dir, "plan-usage-history.json")))
        : ClaudeDesktopUsagePath;

    public string ResolveClaudeDesktopLogPath() => string.IsNullOrWhiteSpace(ClaudeDesktopLogPath)
        ? NewestExisting(ClaudeDesktopDataDirs().Select(dir => Path.Combine(dir, "logs", "claude.ai-web.log")))
        : ClaudeDesktopLogPath;

    // Claude Code's statusline hook (agentcake-statusline.js) mirrors its live
    // payload here; recent Claude Code versions include account rate limits.
    public static string ClaudeCodeStatusPath => Path.Combine(ConfigDir, "claude-status.json");

    // Claude Desktop has stored its data in several places across releases:
    // the current Claude-Data folder, the older MSIX virtualised Roaming folder,
    // and plain %APPDATA%\Claude for non-packaged installs.
    public static IEnumerable<string> ClaudeDesktopDataDirs()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(local, "Claude-Data", "claude_pzs8sxrjxfjjc");
        yield return Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude");
        yield return Path.Combine(roaming, "Claude");
    }

    private static string NewestExisting(IEnumerable<string> candidates)
    {
        var list = candidates.ToList();
        return list.Where(File.Exists)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault() ?? list[0];
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), JsonOptions) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}

public sealed class ProviderSettings
{
    public bool Codex { get; set; } = true;
    public bool ClaudeDesktop { get; set; } = true;
    public bool ClaudeCode { get; set; } = false;
    public bool ChatGpt { get; set; } = false;
    public bool Gemini { get; set; } = false;
    public bool GitHubCopilot { get; set; } = false;
    public bool Cursor { get; set; } = false;
    public bool OpenRouter { get; set; } = false;
    public bool CustomProvider { get; set; } = false;
}
