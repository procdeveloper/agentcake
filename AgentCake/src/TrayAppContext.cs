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
        _tray.DoubleClick += (_, _) => ShowDetails();
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
        var next = IconRenderer.Render(snapshot.Codex, snapshot.Claude);
        var old = _icon;
        _tray.Icon = next;
        _icon = next;
        old?.Dispose();
        _tray.Text = Truncate($"Codex {Display(snapshot.Codex)} · Claude {Display(snapshot.Claude)}", 127);
        _details?.UpdateView(snapshot);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh now", null, (_, _) => KickScan());
        menu.Items.Add("Details…", null, (_, _) => ShowDetails());
        menu.Items.Add("Open AgentCake settings", null, (_, _) => OpenSettings());
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
        if (_last is not null) _details.UpdateView(_last);
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