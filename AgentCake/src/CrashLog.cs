using System.Text;

namespace AgentCake;

internal static class CrashLog
{
    private static readonly string LogPath = Path.Combine(AppSettings.ConfigDir, "agentcake-crash.log");

    public static void Write(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.ConfigDir);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Logging must not fail while processing a separate error.
        }
    }
}
