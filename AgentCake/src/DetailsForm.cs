using System.Drawing;
using System.Runtime.InteropServices;

namespace AgentCake;

public sealed class DetailsForm : Form
{
    private readonly Label _codex = MakeLabel();
    private readonly Label _claude = MakeLabel();
    private readonly Label _footer = MakeLabel(dim: true);

    public DetailsForm(Action refresh)
    {
        Text = "AgentCake usage";
        BackColor = Color.FromArgb(28, 30, 33);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(360, 170);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        _codex.SetBounds(16, 16, 328, 54);
        _claude.SetBounds(16, 76, 328, 54);
        _footer.SetBounds(16, 138, 220, 20);
        var button = new Button { Text = "Refresh", Location = new Point(258, 136), Size = new Size(86, 25) };
        button.Click += (_, _) => refresh();
        Controls.AddRange(new Control[] { _codex, _claude, _footer, button });
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
    public void UpdateView(UsageSnapshot snapshot)
    {
        _codex.Text = Format(snapshot.Codex);
        _claude.Text = Format(snapshot.Claude);
        _footer.Text = $"Updated {snapshot.GeneratedAt:HH:mm:ss}";
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
}