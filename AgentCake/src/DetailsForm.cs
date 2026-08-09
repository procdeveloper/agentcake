using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentCake;

public sealed class DetailsForm : Form
{
    private const int ContentMargin = 24;
    private const int ContentRight = 616;
    private readonly Label _codex = MakeLabel();
    private readonly Label _codexSpark = MakeLabel();
    private readonly Label _claude = MakeLabel();
    private readonly Label _footer = MakeLabel(dim: true);
    private readonly PictureBox _codexIcon = MakeServiceIcon(ServiceIcon.Codex);
    private readonly PictureBox _codexSparkIcon = MakeServiceIcon(ServiceIcon.Codex);
    private readonly PictureBox _claudeIcon = MakeServiceIcon(ServiceIcon.Claude);
    private readonly PictureBox _claudeCodeIcon = MakeServiceIcon(ServiceIcon.ClaudeCode);
    private readonly PictureBox _codexChart = MakeUsageChart();
    private readonly PictureBox _codexSparkChart = MakeUsageChart();
    private readonly PictureBox _claudeChart = MakeUsageChart();
    private readonly PictureBox _claudeCodeChart = MakeUsageChart();
    private readonly PictureBox _codexPace = MakeUsageChart();
    private readonly PictureBox _codexSparkPace = MakeUsageChart();
    private readonly PictureBox _claudePace = MakeUsageChart();
    private readonly PictureBox _claudeCodePace = MakeUsageChart();
    private readonly Label _claudeCode = MakeLabel();
    private readonly PictureBox _agentPortrait = MakeAgentPortrait();
    private readonly Label _heading = MakeHeading();
    private readonly Label _subheading = MakeLabel(dim: true);
    private readonly Panel _headerDivider = MakeDivider();
    private readonly Panel _sourceDivider = MakeDivider();
    private readonly Panel _sourceDivider2 = MakeDivider();
    private readonly Panel _sourceDivider3 = MakeDivider();
    private readonly Panel _footerDivider = MakeDivider();
    private readonly Button _refreshButton = new() { Text = "Refresh", Size = new Size(94, 32) };
    private readonly Button _hideButton = new()
    {
        Text = "×",
        Size = new Size(30, 30),
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        BackColor = Color.FromArgb(54, 57, 62),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI Symbol", 18f, FontStyle.Regular, GraphicsUnit.Pixel),
        Visible = false
    };
    private readonly ToolTip _toolTip = new();
    private bool _stayOnTop;
    private Point? _dragOrigin;

    public DetailsForm(Action refresh)
    {
        Text = "Agent Cake's token buffet";
        Icon = AgentCakeWindowIcon.Load();
        BackColor = Color.FromArgb(28, 30, 33);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(640, 250);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;

        _agentPortrait.SetBounds(ContentMargin, ContentMargin, 68, 68);
        _heading.SetBounds(104, 30, 512, 26);
        _subheading.SetBounds(104, 58, 512, 22);
        _headerDivider.SetBounds(ContentMargin, 104, ContentRight - ContentMargin, 1);
        _subheading.Text = "Live weekly allowance monitor";
        _refreshButton.Click += (_, _) => refresh();
        _hideButton.Text = "\u00d7";
        _hideButton.FlatAppearance.BorderSize = 0;
        _hideButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(82, 86, 92);
        _hideButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(104, 108, 114);
        _hideButton.Location = new Point(ClientSize.Width - _hideButton.Width - 6, 6);
        _hideButton.Click += (_, _) => Hide();
        _toolTip.SetToolTip(_hideButton, "Hide AgentCake (stay-on-top remains enabled)");
        KeyDown += (_, eventArgs) =>
        {
            if (TopMost && eventArgs.KeyCode == Keys.Escape)
            {
                Hide();
                eventArgs.Handled = true;
            }
        };
        WireLaunchAction(AgentLauncher.LaunchCodex, "Click to open Codex", _codexIcon, _codex, _codexChart, _codexPace);
        WireLaunchAction(AgentLauncher.LaunchCodex, "Click to open Codex", _codexSparkIcon, _codexSpark, _codexSparkChart, _codexSparkPace);
        WireLaunchAction(AgentLauncher.LaunchClaudeDesktop, "Click to open Claude Desktop", _claudeIcon, _claude, _claudeChart, _claudePace);
        WireLaunchAction(AgentLauncher.LaunchClaudeCode, "Click to open Command Prompt and run Claude Code", _claudeCodeIcon, _claudeCode, _claudeCodeChart, _claudeCodePace);
        Controls.AddRange(new Control[] { _agentPortrait, _heading, _subheading, _hideButton, _headerDivider, _sourceDivider, _sourceDivider2, _sourceDivider3, _footerDivider, _codexIcon, _codexSparkIcon, _claudeIcon, _claudeCodeIcon, _codexChart, _codexSparkChart, _claudeChart, _claudeCodeChart, _codexPace, _codexSparkPace, _claudePace, _claudeCodePace, _codex, _codexSpark, _claude, _claudeCode, _footer, _refreshButton });
        WireWindowDragAnywhere();
    }

    public void ApplyWindowMode(bool stayOnTop)
    {
        // Borderless is tied to the persistent mode so an always-visible panel
        // never wastes space on a Windows title bar.
        _stayOnTop = stayOnTop;
        FormBorderStyle = stayOnTop ? FormBorderStyle.None : FormBorderStyle.FixedToolWindow;
        TopMost = stayOnTop;
        _hideButton.Visible = stayOnTop;
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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

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
        const int DividerGap = 16;
        int nextRowY = 116;
        int dividerIndex = 0;
        bool hasPrevious = false;
        AddRow(providers.Codex, _codexIcon, _codexChart, _codexPace, _codex, snapshot.Codex, ref nextRowY, ref hasPrevious, ref dividerIndex, DividerGap);
        // An enabled source must remain visible even before its first event in
        // this process. Hiding Spark made it look as though its provider switch
        // had been ignored after an AgentCake restart.
        AddRow(providers.CodexSpark, _codexSparkIcon, _codexSparkChart, _codexSparkPace, _codexSpark, snapshot.CodexSpark, ref nextRowY, ref hasPrevious, ref dividerIndex, DividerGap);
        AddRow(providers.ClaudeDesktop, _claudeIcon, _claudeChart, _claudePace, _claude, snapshot.Claude, ref nextRowY, ref hasPrevious, ref dividerIndex, DividerGap);
        var claudeCode = ServiceUsage.Unavailable("Claude Code", "Launcher ready; live usage reader is not connected yet.");
        AddRow(providers.ClaudeCode, _claudeCodeIcon, _claudeCodeChart, _claudeCodePace, _claudeCode, claudeCode, ref nextRowY, ref hasPrevious, ref dividerIndex, DividerGap);
        _sourceDivider.Visible = dividerIndex > 0;
        _sourceDivider2.Visible = dividerIndex > 1;
        _sourceDivider3.Visible = dividerIndex > 2;

        bool hasAnySource = hasPrevious;
        _footerDivider.Visible = hasAnySource;
        if (hasAnySource)
        {
            _footerDivider.SetBounds(ContentMargin, nextRowY + 8, ContentRight - ContentMargin, 1);
            nextRowY += 22;
        }
        int placeholders = CountEnabledPlaceholders(providers);
        _footer.Text = placeholders == 0
            ? $"Updated {snapshot.GeneratedAt:HH:mm:ss}"
            : $"Updated {snapshot.GeneratedAt:HH:mm:ss} · {placeholders} placeholder(s) enabled";
        _footer.SetBounds(ContentMargin, nextRowY, 466, 20);
        _refreshButton.Location = new Point(522, nextRowY - 6);
        ClientSize = new Size(640, nextRowY + 50);
    }

    private static string Format(ServiceUsage usage)
    {
        if (usage.RemainingPercent is not { } remaining) return $"{usage.Service}: unavailable\n{usage.Detail}";
        string reset = usage.ResetsAt is { } at ? $" · resets {at:ddd HH:mm}" : "";
        string weekly = $"{(usage.FiveHourUsedPercent is null ? "" : "7d: ")}{usage.UsedPercent:0.#}% used{reset}";
        if (usage.FiveHourRemainingPercent is not { } fiveHourRemaining) return $"{usage.Service}: {remaining}% remaining\n{weekly}";
        string fiveHourReset = usage.FiveHourResetsAt is { } fiveHourAt ? $" · resets {fiveHourAt:ddd HH:mm}" : "";
        return $"{usage.Service}: {remaining}% remaining\n{weekly}\n5h: {fiveHourRemaining}% remaining{fiveHourReset}";
    }

    private static Label MakeLabel(bool dim = false) => new()
    {
        AutoSize = false,
        ForeColor = dim ? Color.FromArgb(170, 175, 180) : Color.White,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Panel MakeDivider() => new()
    {
        BackColor = Color.FromArgb(62, 66, 72)
    };

    private void AddSourceDivider(bool visible, ref int nextRowY, ref int dividerIndex, int gap)
    {
        if (!visible) return;
        Panel divider = dividerIndex switch { 0 => _sourceDivider, 1 => _sourceDivider2, _ => _sourceDivider3 };
        divider.SetBounds(ContentMargin, nextRowY + gap / 2, ContentRight - ContentMargin, 1);
        divider.Visible = true;
        dividerIndex++;
        nextRowY += gap;
    }

    private void AddRow(bool visible, PictureBox icon, PictureBox chart, PictureBox pace, Label text, ServiceUsage usage, ref int nextRowY, ref bool hasPrevious, ref int dividerIndex, int dividerGap)
    {
        if (visible && hasPrevious) AddSourceDivider(true, ref nextRowY, ref dividerIndex, dividerGap);
        nextRowY = SetServiceRow(visible, icon, chart, pace, text, usage, nextRowY);
        if (visible) hasPrevious = true;
    }

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
        Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
        Text = "Agent Cake's token buffet",
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void SetChart(PictureBox chart, ServiceUsage usage)
    {
        var old = chart.Image;
        chart.Image = UsagePieRenderer.Render(usage);
        old?.Dispose();
    }

    private static void SetPaceGauge(PictureBox gauge, ServiceUsage usage)
    {
        var old = gauge.Image;
        gauge.Image = UsagePaceGaugeRenderer.Render(usage);
        old?.Dispose();
    }

    private static int SetServiceRow(bool visible, PictureBox icon, PictureBox chart, PictureBox pace, Label text, ServiceUsage usage, int y, bool hasExtraLine = false)
    {
        icon.Visible = visible;
        chart.Visible = visible;
        pace.Visible = visible;
        text.Visible = visible;
        if (!visible) return y;

        int blockHeight = usage.FiveHourUsedPercent is not null ? 78 : hasExtraLine ? 76 : 54;
        icon.SetBounds(ContentMargin, y + (blockHeight - 40) / 2, 40, 40);
        chart.SetBounds(422, y + (blockHeight - 52) / 2, 52, 52);
        pace.SetBounds(494, y + (blockHeight - 72) / 2, 104, 72);
        text.SetBounds(74, y, 338, blockHeight);
        text.Text = Format(usage);
        SetChart(chart, usage);
        SetPaceGauge(pace, usage);
        return y + blockHeight + 6;
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

    private void WireWindowDragAnywhere()
    {
        WireWindowDrag(this);
        foreach (Control control in Controls) WireWindowDrag(control);
    }

    private void WireWindowDrag(Control control)
    {
        control.MouseDown += (_, eventArgs) =>
        {
            if (_stayOnTop && eventArgs.Button == MouseButtons.Left)
                _dragOrigin = Control.MousePosition;
        };
        control.MouseUp += (_, _) => _dragOrigin = null;
        control.MouseMove += (_, eventArgs) =>
        {
            if (!_stayOnTop || _dragOrigin is not { } origin || eventArgs.Button != MouseButtons.Left) return;

            Size threshold = SystemInformation.DragSize;
            Point current = Control.MousePosition;
            if (Math.Abs(current.X - origin.X) < threshold.Width / 2
                && Math.Abs(current.Y - origin.Y) < threshold.Height / 2) return;

            // Preserve normal control clicks until the pointer actually moves.
            // Once it does, hand off to Windows' native title-bar drag behaviour.
            _dragOrigin = null;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
        };
        control.MouseCaptureChanged += (_, _) =>
        {
            if (!Control.MouseButtons.HasFlag(MouseButtons.Left)) _dragOrigin = null;
        };
    }

}

internal static class UsagePieRenderer
{
    private static readonly Color Track = Color.FromArgb(58, 62, 68);
    private static readonly Color Normal = Color.FromArgb(65, 150, 100);
    private static readonly Color Warning = Color.FromArgb(241, 205, 76);
    private static readonly Color Critical = Color.FromArgb(244, 161, 174);
    private static readonly Color TimeLeft = Color.FromArgb(91, 169, 255);
    private static readonly Color TimeLeftTrack = Color.FromArgb(43, 73, 108);

    public static Bitmap Render(ServiceUsage usage)
    {
        const int canvasSize = 52;
        var bitmap = new Bitmap(canvasSize, canvasSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        // The 36px weekly pie retains its original size inside a larger canvas
        // that leaves room for its allowance and time-to-reset rims.
        var circle = new Rectangle(8, 8, 36, 36);

        if (usage.FiveHourUsedPercent is { } fiveHourUsed)
        {
            var rim = new Rectangle(5, 5, 42, 42);
            using var rimTrack = new Pen(Track, 4.5f);
            graphics.DrawEllipse(rimTrack, rim);
            if (fiveHourUsed > 0)
            {
                float sweep = (float)Math.Min(Math.Clamp(fiveHourUsed, 0d, 100d) * 3.6d, 359.9d);
                using var rimFill = new Pen(UsageColor(fiveHourUsed), 4.5f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
                graphics.DrawArc(rimFill, rim, -90, sweep);
            }
        }

        int? weeklyTimeLeft = WeeklyTimeLeftPercent(usage);
        if (weeklyTimeLeft is { } timeLeft)
        {
            var timeRim = new Rectangle(1, 1, 50, 50);
            using var timeTrack = new Pen(TimeLeftTrack, 2f);
            graphics.DrawEllipse(timeTrack, timeRim);
            if (timeLeft > 0)
            {
                float sweep = (float)Math.Min(timeLeft * 3.6d, 359.9d);
                using var timeFill = new Pen(TimeLeft, 2f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
                // The free end retreats counterclockwise as time remaining declines.
                graphics.DrawArc(timeFill, timeRim, -90, sweep);
            }
        }

        using (var track = new SolidBrush(Track)) graphics.FillEllipse(track, circle);
        if (usage.UsedPercent is { } used)
        {
            float sweep = (float)(Math.Clamp(used, 0d, 100d) * 3.6d);
            using var fill = new SolidBrush(UsageColor(used));
            graphics.FillPie(fill, circle, -90, sweep);
        }

        string label = usage.RemainingPercent is { } remaining ? remaining.ToString() : "--";
        using var font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
        var labelSize = graphics.MeasureString(label, font);
        using var shadow = new SolidBrush(Color.FromArgb(150, Color.Black));
        using var text = new SolidBrush(Color.White);
        float x = (canvasSize - labelSize.Width) / 2f;
        float y = (canvasSize - labelSize.Height) / 2f - 1f;
        graphics.DrawString(label, font, shadow, x + 1, y + 1);
        graphics.DrawString(label, font, text, x, y);
        return bitmap;
    }

    private static Color UsageColor(double used) => used >= 80 ? Critical : used >= 65 ? Warning : Normal;

    private static int? WeeklyTimeLeftPercent(ServiceUsage usage)
    {
        if (usage.ResetsAt is not { } resetsAt || usage.WeeklyWindow is not { } weeklyWindow) return null;
        double percent = (resetsAt - DateTime.Now).TotalMinutes / weeklyWindow.TotalMinutes * 100d;
        return (int)Math.Round(Math.Clamp(percent, 0d, 100d));
    }
}

internal static class UsagePaceGaugeRenderer
{
    private static readonly Color Track = Color.FromArgb(56, 60, 66);
    private static readonly Color Normal = Color.FromArgb(65, 150, 100);
    private static readonly Color Warning = Color.FromArgb(241, 205, 76);
    private static readonly Color Critical = Color.FromArgb(244, 161, 174);

    public static Bitmap Render(ServiceUsage usage)
    {
        const int width = 104;
        const int height = 72;
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        // A compact 240° tach sweep: its ends remain inside the canvas instead
        // of being cut off at the lower edge of the provider row.
        var dial = new Rectangle(24, 4, 56, 56);
        using (var track = new Pen(Track, 4f)) graphics.DrawArc(track, dial, 150, 240);
        DrawBand(graphics, dial, 150, RatioToSweep(1), Normal);
        DrawBand(graphics, dial, 150 + RatioToSweep(1), RatioToSweep(1.3) - RatioToSweep(1), Warning);
        DrawBand(graphics, dial, 150 + RatioToSweep(1.3), 240 - RatioToSweep(1.3), Critical);

        foreach (double tickRatio in new[] { 0d, 0.25, 0.5, 1d, 1.3, 2d, 4d, 7d, 10d })
        {
            DrawTick(graphics, 150 + RatioToSweep(tickRatio));
        }

        if (usage.BurnPaceRatio is { } ratio)
        {
            // 1.0x is the sustainable pace; the logarithmic dial leaves room up to 10x.
            float angle = 150 + RatioToSweep(ratio);
            DrawNeedle(graphics, angle);
        }

        using var hub = new SolidBrush(Color.FromArgb(91, 185, 255));
        graphics.FillEllipse(hub, 47, 27, 10, 10);
        string label = usage.BurnPaceRatio is { } pace ? $"{pace:0.0}x" : "--";
        using var font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
        var labelSize = graphics.MeasureString(label, font);
        using var text = new SolidBrush(Color.White);
        graphics.DrawString(label, font, text, (width - labelSize.Width) / 2f, 47);
        return bitmap;
    }

    private static void DrawBand(Graphics graphics, Rectangle dial, float start, float sweep, Color color)
    {
        using var pen = new Pen(color, 4f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        graphics.DrawArc(pen, dial, start, sweep);
    }

    private static void DrawTick(Graphics graphics, float degrees)
    {
        double radians = degrees * Math.PI / 180d;
        var center = new PointF(52, 32);
        // Deliberately cross the coloured 28px-radius band for a proper RPM dial.
        var outer = new PointF(center.X + (float)(30 * Math.Cos(radians)), center.Y + (float)(30 * Math.Sin(radians)));
        var inner = new PointF(center.X + (float)(22 * Math.Cos(radians)), center.Y + (float)(22 * Math.Sin(radians)));
        using var pen = new Pen(Color.FromArgb(165, 170, 178), 1f);
        graphics.DrawLine(pen, inner, outer);
    }

    private static void DrawNeedle(Graphics graphics, float degrees)
    {
        double radians = degrees * Math.PI / 180d;
        var center = new PointF(52, 32);
        var tip = new PointF(center.X + (float)(23 * Math.Cos(radians)), center.Y + (float)(23 * Math.Sin(radians)));
        using var shadow = new Pen(Color.FromArgb(190, Color.Black), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var needle = new Pen(Color.FromArgb(59, 183, 255), 3.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var highlight = new Pen(Color.FromArgb(202, 240, 255), 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(shadow, center.X + 1, center.Y + 1, tip.X + 1, tip.Y + 1);
        graphics.DrawLine(needle, center, tip);
        graphics.DrawLine(highlight, center, tip);
    }

    private static float RatioToSweep(double ratio)
    {
        // A linear 0-10x dial would hide the useful 0-2x range. Logarithmic
        // spacing keeps the sustainable 1x threshold readable while red ends at 10x.
        return (float)(Math.Log(1 + Math.Clamp(ratio, 0d, 10d)) / Math.Log(11) * 240d);
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
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
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
