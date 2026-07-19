using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentCake;

public sealed class DetailsForm : Form
{
    private readonly Label _codex = MakeLabel();
    private readonly Label _claude = MakeLabel();
    private readonly Label _footer = MakeLabel(dim: true);
    private readonly PictureBox _codexIcon = MakeServiceIcon(ServiceIcon.Codex);
    private readonly PictureBox _claudeIcon = MakeServiceIcon(ServiceIcon.Claude);
    private readonly PictureBox _claudeCodeIcon = MakeServiceIcon(ServiceIcon.ClaudeCode);
    private readonly PictureBox _codexChart = MakeUsageChart();
    private readonly PictureBox _claudeChart = MakeUsageChart();
    private readonly PictureBox _claudeCodeChart = MakeUsageChart();
    private readonly Label _claudeCode = MakeLabel();
    private readonly PictureBox _agentPortrait = MakeAgentPortrait();
    private readonly Label _heading = MakeHeading();
    private readonly Label _subheading = MakeLabel(dim: true);
    private readonly Button _refreshButton = new() { Text = "Refresh", Size = new Size(94, 32) };
    private readonly ToolTip _toolTip = new();

    public DetailsForm(Action refresh)
    {
        Text = "AgentCake usage monitor";
        Icon = AgentCakeWindowIcon.Load();
        BackColor = Color.FromArgb(28, 30, 33);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(390, 250);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        _agentPortrait.SetBounds(16, 12, 68, 68);
        _heading.SetBounds(96, 18, 278, 26);
        _subheading.SetBounds(96, 46, 278, 22);
        _subheading.Text = "Live weekly allowance monitor";
        _refreshButton.Click += (_, _) => refresh();
        WireLaunchAction(AgentLauncher.LaunchCodex, "Click to open Codex", _codexIcon, _codex, _codexChart);
        WireLaunchAction(AgentLauncher.LaunchClaudeDesktop, "Click to open Claude Desktop", _claudeIcon, _claude, _claudeChart);
        WireLaunchAction(AgentLauncher.LaunchClaudeCode, "Click to open Command Prompt and run Claude Code", _claudeCodeIcon, _claudeCode, _claudeCodeChart);
        Controls.AddRange(new Control[] { _agentPortrait, _heading, _subheading, _codexIcon, _claudeIcon, _claudeCodeIcon, _codexChart, _claudeChart, _claudeCodeChart, _codex, _claude, _claudeCode, _footer, _refreshButton });
    }

    public void PositionNearTray()
    {
        IntPtr shell = FindWindow("Shell_TrayWnd", null);
        IntPtr notificationArea = shell == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null);
        if (shell == IntPtr.Zero || !GetWindowRect(shell, out var taskbar))
        {
            CenterToScreen();
            return;
        }

        var screen = Screen.FromHandle(shell).Bounds;
        var anchor = notificationArea != IntPtr.Zero && GetWindowRect(notificationArea, out var notify) ? notify : taskbar;
        int x;
        int y;
        bool horizontal = taskbar.Width >= taskbar.Height;

        if (horizontal && taskbar.Top >= screen.Top + screen.Height / 2)
        {
            x = anchor.Right - Width;
            y = taskbar.Top - Height - 8;
        }
        else if (horizontal)
        {
            x = anchor.Right - Width;
            y = taskbar.Bottom + 8;
        }
        else if (taskbar.Left < screen.Left + screen.Width / 2)
        {
            x = taskbar.Right + 8;
            y = anchor.Bottom - Height;
        }
        else
        {
            x = taskbar.Left - Width - 8;
            y = anchor.Bottom - Height;
        }

        Location = new Point(
            Math.Clamp(x, screen.Left + 8, screen.Right - Width - 8),
            Math.Clamp(y, screen.Top + 8, screen.Bottom - Height - 8));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public void UpdateView(UsageSnapshot snapshot, ProviderSettings providers)
    {
        int nextRowY = 94;
        nextRowY = SetServiceRow(providers.Codex, _codexIcon, _codexChart, _codex, snapshot.Codex, nextRowY);
        nextRowY = SetServiceRow(providers.ClaudeDesktop, _claudeIcon, _claudeChart, _claude, snapshot.Claude, nextRowY);
        var claudeCode = ServiceUsage.Unavailable("Claude Code", "Launcher ready; live usage reader is not connected yet.");
        nextRowY = SetServiceRow(providers.ClaudeCode, _claudeCodeIcon, _claudeCodeChart, _claudeCode, claudeCode, nextRowY);
        int placeholders = CountEnabledPlaceholders(providers);
        _footer.Text = placeholders == 0
            ? $"Updated {snapshot.GeneratedAt:HH:mm:ss}"
            : $"Updated {snapshot.GeneratedAt:HH:mm:ss} · {placeholders} placeholder(s) enabled";
        _footer.SetBounds(16, nextRowY - 1, 250, 20);
        _refreshButton.Location = new Point(280, nextRowY - 9);
        ClientSize = new Size(390, nextRowY + 36);
    }

    private static string Format(ServiceUsage usage)
    {
        if (usage.RemainingPercent is not { } remaining) return $"{usage.Service}: unavailable\n{usage.Detail}";
        string reset = usage.ResetsAt is { } at ? $" · resets {at:ddd HH:mm}" : "";
        return $"{usage.Service}: {remaining}% remaining\n{usage.UsedPercent:0.#}% used{reset}";
    }

    private static Label MakeLabel(bool dim = false) => new()
    {
        AutoSize = false,
        ForeColor = dim ? Color.FromArgb(170, 175, 180) : Color.White,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static PictureBox MakeServiceIcon(ServiceIcon service) => new()
    {
        Image = ServiceIconRenderer.Render(service),
        SizeMode = PictureBoxSizeMode.CenterImage,
        BackColor = Color.Transparent
    };

    private static PictureBox MakeUsageChart() => new()
    {
        SizeMode = PictureBoxSizeMode.CenterImage,
        BackColor = Color.Transparent
    };

    private static PictureBox MakeAgentPortrait() => new()
    {
        Image = AgentCakePortrait.Load(),
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.Transparent
    };

    private static Label MakeHeading() => new()
    {
        AutoSize = false,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        Text = "AgentCake usage monitor",
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void SetChart(PictureBox chart, ServiceUsage usage)
    {
        var old = chart.Image;
        chart.Image = UsagePieRenderer.Render(usage);
        old?.Dispose();
    }

    private static int SetServiceRow(bool visible, PictureBox icon, PictureBox chart, Label text, ServiceUsage usage, int y)
    {
        icon.Visible = visible;
        chart.Visible = visible;
        text.Visible = visible;
        if (!visible) return y;

        icon.SetBounds(16, y + 3, 40, 40);
        chart.SetBounds(334, y + 3, 40, 40);
        text.SetBounds(66, y, 260, 54);
        text.Text = Format(usage);
        SetChart(chart, usage);
        return y + 60;
    }

    private static int CountEnabledPlaceholders(ProviderSettings providers) => new[]
    {
        providers.ClaudeCode,
        providers.ChatGpt,
        providers.Gemini,
        providers.GitHubCopilot,
        providers.Cursor,
        providers.OpenRouter,
        providers.CustomProvider
    }.Count(value => value);

    private void WireLaunchAction(Action launch, string tooltip, params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.Cursor = Cursors.Hand;
            _toolTip.SetToolTip(control, tooltip);
            control.Click += (_, _) => launch();
        }
    }
}

internal static class UsagePieRenderer
{
    private static readonly Color Track = Color.FromArgb(58, 62, 68);
    private static readonly Color Normal = Color.FromArgb(65, 150, 100);
    private static readonly Color Warning = Color.FromArgb(241, 205, 76);
    private static readonly Color Critical = Color.FromArgb(244, 161, 174);

    public static Bitmap Render(ServiceUsage usage)
    {
        var bitmap = new Bitmap(40, 40);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var circle = new Rectangle(2, 2, 36, 36);

        using (var track = new SolidBrush(Track)) graphics.FillEllipse(track, circle);
        if (usage.UsedPercent is { } used)
        {
            float sweep = (float)(Math.Clamp(used, 0d, 100d) * 3.6d);
            using var fill = new SolidBrush(used >= 80 ? Critical : used >= 65 ? Warning : Normal);
            graphics.FillPie(fill, circle, -90, sweep);
        }

        string label = usage.RemainingPercent is { } remaining ? remaining.ToString() : "--";
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel);
        var size = graphics.MeasureString(label, font);
        using var shadow = new SolidBrush(Color.FromArgb(150, Color.Black));
        using var text = new SolidBrush(Color.White);
        float x = (40 - size.Width) / 2f;
        float y = (40 - size.Height) / 2f - 1f;
        graphics.DrawString(label, font, shadow, x + 1, y + 1);
        graphics.DrawString(label, font, text, x, y);
        return bitmap;
    }
}

internal enum ServiceIcon { Codex, Claude, ClaudeCode }

internal static class ServiceIconRenderer
{
    public static Bitmap Render(ServiceIcon service)
    {
        var bitmap = new Bitmap(40, 40);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        if (service == ServiceIcon.Codex)
        {
            using var background = new SolidBrush(Color.FromArgb(30, 39, 65));
            using var stroke = new Pen(Color.FromArgb(108, 190, 255), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.FillEllipse(background, 1, 1, 38, 38);
            for (int rotation = 0; rotation < 360; rotation += 60)
            {
                var state = graphics.Save();
                graphics.TranslateTransform(20, 20);
                graphics.RotateTransform(rotation);
                graphics.DrawArc(stroke, -9, -14, 18, 20, 204, 132);
                graphics.Restore(state);
            }
            using var core = new SolidBrush(Color.FromArgb(108, 190, 255));
            graphics.FillEllipse(core, 17, 17, 6, 6);
        }
        else if (service == ServiceIcon.Claude)
        {
            using var background = new SolidBrush(Color.FromArgb(214, 92, 55));
            using var stroke = new Pen(Color.White, 3.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.FillEllipse(background, 1, 1, 38, 38);
            graphics.TranslateTransform(20, 20);
            for (int rotation = 0; rotation < 360; rotation += 60)
            {
                var state = graphics.Save();
                graphics.RotateTransform(rotation);
                graphics.DrawLine(stroke, 0, -12, 0, 12);
                graphics.Restore(state);
            }
            using var core = new SolidBrush(Color.White);
            graphics.FillEllipse(core, -3, -3, 6, 6);
        }
        else
        {
            using var background = new SolidBrush(Color.FromArgb(52, 55, 61));
            using var text = new SolidBrush(Color.FromArgb(224, 228, 232));
            using var font = new Font("Cascadia Mono", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.FillEllipse(background, 1, 1, 38, 38);
            graphics.DrawString(">_", font, text, 7, 11);
        }

        return bitmap;
    }
}

internal static class AgentCakeWindowIcon
{
    public static Icon Load()
    {
        using var source = AgentCakePortrait.Load();
        if (source is null) return SystemIcons.Application;
        using var scaled = new Bitmap(source, new Size(32, 32));
        IntPtr handle = scaled.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally { DeleteObject(handle); }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}

internal static class AgentCakePortrait
{
    public static Bitmap? Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "agentcake-profile.png");
        if (File.Exists(path))
        {
            using var source = new Bitmap(path);
            return new Bitmap(source);
        }

        using var stream = typeof(AgentCakePortrait).Assembly.GetManifestResourceStream("AgentCake.assets.agentcake-profile.png");
        if (stream is null) return null;
        using var embedded = new Bitmap(stream);
        return new Bitmap(embedded);
    }
}

internal static class AgentLauncher
{
    private const string CodexAppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
    private const string ClaudeDesktopAppId = "Claude_pzs8sxrjxfjjc!Claude";

    public static void LaunchCodex() => LaunchWindowsApp(CodexAppId);

    public static void LaunchClaudeDesktop() => LaunchWindowsApp(ClaudeDesktopAppId);

    public static void LaunchClaudeCode()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k claude") { UseShellExecute = true });
        }
        catch { }
    }

    private static void LaunchWindowsApp(string appId)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
        }
        catch { }
    }
}
