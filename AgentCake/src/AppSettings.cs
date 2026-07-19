using System.Text.Json;

namespace AgentCake;

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 30;
    public string CodexSessionsDir { get; set; } = "";
    public string ClaudeStatusPath { get; set; } = "";

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentCake");
    public static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

    public string ResolveCodexSessionsDir() => string.IsNullOrWhiteSpace(CodexSessionsDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions")
        : CodexSessionsDir;

    public string ResolveClaudeStatusPath() => string.IsNullOrWhiteSpace(ClaudeStatusPath)
        ? Path.Combine(ConfigDir, "claude-status.json")
        : ClaudeStatusPath;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}