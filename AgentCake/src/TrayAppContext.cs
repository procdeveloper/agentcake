using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AgentCake;

/// <summary>Owns the tray icon, the refresh timer, and the context menu. No main window.</summary>
public sealed class TrayAppContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AgentCake";

    private readonly AppSettings _settings;
    private readonly UsageReader _reader;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Control _marshal; // hidden control to marshal back onto the UI thread

    private Icon? _currentIcon;
    private bool _scanning;
    private UsageSnapshot? _last;
    private DetailsForm? _details;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();
        _reader = new UsageReader(() => _settings);

        // Force handle creation on this (the main STA) thread so background scans can
        // BeginInvoke back onto it. Application.Run pumps this same thread's message loop.
        _marshal = new Control();
        _ = _marshal.Handle;

        _tray = new NotifyIcon
        {
            Visible = true,
            Icon = IconRenderer.Render(0, 0),
            Text = "Claude usage — starting…"
        };
        _currentIcon = _tray.Icon;
        _tray.DoubleClick += (_, _) => ShowDetails();
        _tray.ContextMenuStrip = BuildMenu();

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(3, _settings.RefreshSeconds) * 1000 };
        _timer.Tick += (_, _) => KickScan();
        _timer.Start();

        KickScan(); // immediate first read
    }

    // ---- scanning ----

    private void KickScan()
    {
        if (_scanning) return;
        _scanning = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            UsageSnapshot snap;
            try { snap = _reader.Scan(); }
            catch { _scanning = false; return; }
            try
            {
                if (_marshal.IsHandleCreated && !_marshal.IsDisposed)
                    _marshal.BeginInvoke((Action)(() => ApplySnapshot(snap)));
                else
                    _scanning = false;
            }
            catch { _scanning = false; }
        });
    }

    private void ApplySnapshot(UsageSnapshot snap)
    {
        _scanning = false;
        _last = snap;

        double f5h = (double)snap.CurrentWindow.Billable(_settings.CountCacheReadsInWindow) / _settings.Window5hCapTokens;
        double fwk = (double)snap.Week.Billable(_settings.CountCacheReadsInWindow) / _settings.EffectiveWeeklyCapTokens;

        SwapIcon(IconRenderer.Render(f5h, fwk));
        _tray.Text = Truncate(BuildTooltip(snap, f5h, fwk), 127);
        _details?.UpdateView(snap, _settings);
    }

    private void SwapIcon(Icon next)
    {
        var prev = _currentIcon;
        _tray.Icon = next;
        _currentIcon = next;
        prev?.Dispose(); // frees the previous HICON
    }

    private string BuildTooltip(UsageSnapshot s, double f5h, double fwk)
    {
        if (!s.DataDirExists)
            return $"No Claude Code logs at {s.DataDir}";

        string w5 = $"5h {Pct(f5h)}  ({Short(s.CurrentWindow.Billable(_settings.CountCacheReadsInWindow))}/{Short(_settings.Window5hCapTokens)})";
        if (s.WindowEndsAt is { } e) w5 += $" resets {e:HH:mm}";
        string wk = $"wk {Pct(fwk)}  ({Short(s.Week.Billable(_settings.CountCacheReadsInWindow))}/{Short(_settings.EffectiveWeeklyCapTokens)})";
        return $"Claude usage\n{w5}\n{wk}";
    }

    // ---- menu ----

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var header = new ToolStripMenuItem("Claude usage") { Enabled = false };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Refresh now", null, (_, _) => KickScan());
        menu.Items.Add("Details…", null, (_, _) => ShowDetails());

        var plan = new ToolStripMenuItem("Plan");
        foreach (PlanTier t in Enum.GetValues<PlanTier>())
        {
            var item = new ToolStripMenuItem(PlanLabel(t)) { Checked = _settings.Plan == t, CheckOnClick = false };
            item.Click += (_, _) =>
            {
                _settings.Plan = t;
                _settings.Save();
                foreach (ToolStripMenuItem mi in plan.DropDownItems) mi.Checked = false;
                item.Checked = true;
                KickScan();
            };
            plan.DropDownItems.Add(item);
        }
        menu.Items.Add(plan);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit settings.json…", null, (_, _) => OpenSettingsFile());
        menu.Items.Add("Open .claude folder", null, (_, _) => OpenClaudeFolder());

        var startup = new ToolStripMenuItem("Run at login") { Checked = IsRunAtLogin(), CheckOnClick = true };
        startup.Click += (_, _) => SetRunAtLogin(startup.Checked);
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        return menu;
    }

    private static string PlanLabel(PlanTier t) => t switch
    {
        PlanTier.Pro => "Pro (~44k / 5h)",
        PlanTier.Max5 => "Max 5x (~88k / 5h)",
        PlanTier.Max20 => "Max 20x (~220k / 5h)",
        _ => "Custom (settings.json)"
    };

    private void ShowDetails()
    {
        if (_details is null || _details.IsDisposed)
        {
            _details = new DetailsForm(() => KickScan());
            _details.FormClosed += (_, _) => _details = null;
        }
        if (_last is not null) _details.UpdateView(_last, _settings);
        _details.Show();
        _details.BringToFront();
        _details.Activate();
    }

    private void OpenSettingsFile()
    {
        _settings.Save(); // ensure the file exists
        try { Process.Start(new ProcessStartInfo(AppSettings.ConfigPath) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private void OpenClaudeFolder()
    {
        string dir = _reader.ResolveDataDir();
        try
        {
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    // ---- run at login (HKCU Run key; user-toggled) ----

    private static bool IsRunAtLogin()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(RunValue) is string;
        }
        catch { return false; }
    }

    private static void SetRunAtLogin(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enable) key.SetValue(RunValue, $"\"{Application.ExecutablePath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch { /* ignore */ }
    }

    // ---- helpers ----

    private static string Pct(double f) => $"{Math.Min(f, 9.99) * 100:0}%";

    private static string Short(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:0.#}M";
        if (n >= 1_000) return $"{n / 1_000.0:0.#}k";
        return n.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private void Quit()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _currentIcon?.Dispose();
        _marshal.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Dispose();
            _tray?.Dispose();
            _currentIcon?.Dispose();
            _marshal?.Dispose();
        }
        base.Dispose(disposing);
    }
}
