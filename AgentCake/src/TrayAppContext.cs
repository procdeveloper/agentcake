using System.Diagnostics;
using Microsoft.Win32;

namespace AgentCake;

public sealed class TrayAppContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AgentCake";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UsageReader _reader;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Control _marshal = new();
    private Icon? _icon;
    private int _scanning;
    private UsageSnapshot? _last;
    private DetailsForm? _details;

    public TrayAppContext()
    {
        _reader = new UsageReader(() => _settings);
        _ = _marshal.Handle;
        _tray = new NotifyIcon { Visible = true, Text = "AgentCake: loading live limits" };
        _tray.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left) ShowDetails();
        };
        _tray.ContextMenuStrip = BuildMenu();
        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(10, _settings.RefreshSeconds) * 1000 };
        _timer.Tick += (_, _) => KickScan();
        _timer.Start();
        KickScan();
    }

    private void KickScan()
    {
        if (Interlocked.Exchange(ref _scanning, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            UsageSnapshot snapshot;
            try { snapshot = _reader.Scan(); }
            catch
            {
                Interlocked.Exchange(ref _scanning, 0);
                return;
            }
            try { _marshal.BeginInvoke((Action)(() => ApplySnapshot(snapshot))); }
            catch { Interlocked.Exchange(ref _scanning, 0); }
        });
    }

    private void ApplySnapshot(UsageSnapshot snapshot)
    {
        Interlocked.Exchange(ref _scanning, 0);
        _last = snapshot;
        var enabledUsage = GetEnabledLive(snapshot);
        var next = _settings.ShowUsageBarsInTray && enabledUsage.Count > 0
            ? IconRenderer.Render(enabledUsage)
            : AgentCakeWindowIcon.Load();
        var old = _icon;
        _tray.Icon = next;
        _icon = next;
        old?.Dispose();
        _tray.Text = enabledUsage.Count == 0
            ? "AgentCake: no live providers enabled"
            : Truncate(string.Join(" · ", enabledUsage.Select(usage => $"{usage.Service} {Display(usage)}")), 127);
        _details?.UpdateView(snapshot, _settings.Providers);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, (_, _) => KickScan());
        menu.Items.Add("Details...", null, (_, _) => ShowDetails());
        menu.Items.Add("Open AgentCake settings", null, (_, _) => OpenSettings());
        menu.Items.Add(BuildProvidersMenu());
        var bars = new ToolStripMenuItem("Show usage bars in tray") { Checked = _settings.ShowUsageBarsInTray, CheckOnClick = true };
        bars.Click += (_, _) => SetUsageBarsInTray(bars.Checked);
        menu.Items.Add(bars);
        var startup = new ToolStripMenuItem("Run at login") { Checked = IsRunAtLogin(), CheckOnClick = true };
        startup.Click += (_, _) => SetRunAtLogin(startup.Checked);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        return menu;
    }

    private void ShowDetails()
    {
        if (_details is null || _details.IsDisposed)
        {
            _details = new DetailsForm(KickScan);
            _details.FormClosed += (_, _) => _details = null;
        }
        if (_last is not null) _details.UpdateView(_last, _settings.Providers);
        _details.PositionNearTray();
        _details.Show();
        _details.Activate();
    }

    private static string Display(ServiceUsage usage) => usage.RemainingPercent is { } remaining ? $"{remaining}% remaining" : "unavailable";
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private void OpenSettings()
    {
        _settings.Save();
        try { Process.Start(new ProcessStartInfo(AppSettings.ConfigPath) { UseShellExecute = true }); } catch { }
    }

    private void SetUsageBarsInTray(bool enabled)
    {
        _settings.ShowUsageBarsInTray = enabled;
        _settings.Save();
        if (_last is not null) ApplySnapshot(_last);
    }

    private List<ServiceUsage> GetEnabledLive(UsageSnapshot snapshot)
    {
        var enabled = new List<ServiceUsage>();
        if (_settings.Providers.Codex) enabled.Add(snapshot.Codex);
        if (_settings.Providers.ClaudeDesktop) enabled.Add(snapshot.Claude);
        return enabled;
    }

    private ToolStripMenuItem BuildProvidersMenu()
    {
        var menu = new ToolStripMenuItem("Providers");
        AddProvider(menu, "Codex", () => _settings.Providers.Codex, value => _settings.Providers.Codex = value);
        AddProvider(menu, "Claude Desktop", () => _settings.Providers.ClaudeDesktop, value => _settings.Providers.ClaudeDesktop = value);
        menu.DropDownItems.Add(new ToolStripSeparator());
        AddProvider(menu, "Claude Code (placeholder)", () => _settings.Providers.ClaudeCode, value => _settings.Providers.ClaudeCode = value);
        AddProvider(menu, "ChatGPT (placeholder)", () => _settings.Providers.ChatGpt, value => _settings.Providers.ChatGpt = value);
        AddProvider(menu, "Gemini (placeholder)", () => _settings.Providers.Gemini, value => _settings.Providers.Gemini = value);
        AddProvider(menu, "GitHub Copilot (placeholder)", () => _settings.Providers.GitHubCopilot, value => _settings.Providers.GitHubCopilot = value);
        AddProvider(menu, "Cursor (placeholder)", () => _settings.Providers.Cursor, value => _settings.Providers.Cursor = value);
        AddProvider(menu, "OpenRouter (placeholder)", () => _settings.Providers.OpenRouter, value => _settings.Providers.OpenRouter = value);
        AddProvider(menu, "Custom provider (placeholder)", () => _settings.Providers.CustomProvider, value => _settings.Providers.CustomProvider = value);
        return menu;
    }

    private void AddProvider(ToolStripMenuItem menu, string title, Func<bool> get, Action<bool> set)
    {
        var item = new ToolStripMenuItem(title) { Checked = get(), CheckOnClick = true };
        item.Click += (_, _) =>
        {
            set(item.Checked);
            _settings.Save();
            if (_last is not null) ApplySnapshot(_last);
        };
        menu.DropDownItems.Add(item);
    }

    private static bool IsRunAtLogin()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(RunValue) is string; }
        catch { return false; }
    }

    private static void SetRunAtLogin(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key?.SetValue(RunValue, $"\"{Application.ExecutablePath}\"");
            else key?.DeleteValue(RunValue, false);
        }
        catch { }
    }

    private void Quit()
    {
        _timer.Stop();
        _tray.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _icon?.Dispose();
            _marshal.Dispose();
        }
        base.Dispose(disposing);
    }
}
