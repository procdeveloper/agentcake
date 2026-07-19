using System.Text.Json;

namespace AgentCake;

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 30;
    public bool ShowUsageBarsInTray { get; set; } = false;
    public ProviderSettings Providers { get; set; } = new();
    public string CodexSessionsDir { get; set; } = "";
    public string ClaudeDesktopUsagePath { get; set; } = "";

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCake");
    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public string ResolveCodexSessionsDir() => string.IsNullOrWhiteSpace(CodexSessionsDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions")
        : CodexSessionsDir;

    public string ResolveClaudeDesktopUsagePath() => string.IsNullOrWhiteSpace(ClaudeDesktopUsagePath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "plan-usage-history.json")
        : ClaudeDesktopUsagePath;

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
