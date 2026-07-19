using System.Drawing;

namespace AgentCake;

/// <summary>Small popup showing the 5-hour and weekly gauges with exact numbers and reset times.</summary>
public sealed class DetailsForm : Form
{
    private static readonly Color Bg = Color.FromArgb(0x14, 0x16, 0x15);
    private static readonly Color Fg = Color.FromArgb(0xEC, 0xF1, 0xEE);
    private static readonly Color Dim = Color.FromArgb(0x9A, 0xA3, 0x9F);
    private static readonly Color TrackCol = Color.FromArgb(0x2A, 0x2E, 0x2C);
    private static readonly Color Green = Color.FromArgb(0x39, 0xD3, 0x53);
    private static readonly Color Amber = Color.FromArgb(0xF5, 0xB8, 0x2E);
    private static readonly Color Red = Color.FromArgb(0xF0, 0x4A, 0x3A);

    private readonly Label _w5Title = Mk(bold: true);
    private readonly Label _w5Value = Mk();
    private readonly Label _w5Reset = Mk(dim: true);
    private readonly Panel _w5Track = MkTrack();
    private readonly Panel _w5Fill = MkFill();

    private readonly Label _wkTitle = Mk(bold: true);
    private readonly Label _wkValue = Mk();
    private readonly Label _wkReset = Mk(dim: true);
    private readonly Panel _wkTrack = MkTrack();
    private readonly Panel _wkFill = MkFill();

    private readonly Label _footer = Mk(dim: true);

    public DetailsForm(Action onRefresh)
    {
        Text = "Claude usage";
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(380, 250);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        var wa = Screen.PrimaryScreen!.WorkingArea; // bottom-right, above the tray
        Location = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);

        _w5Track.Controls.Add(_w5Fill);
        _wkTrack.Controls.Add(_wkFill);

        Layout5(_w5Title, _w5Value, _w5Reset, _w5Track, 16);
        Layout5(_wkTitle, _wkValue, _wkReset, _wkTrack, 116);

        var refresh = new Button
        {
            Text = "Refresh",
            FlatStyle = FlatStyle.Flat,
            ForeColor = Fg,
            Location = new Point(16, 210),
            Size = new Size(90, 26)
        };
        refresh.FlatAppearance.BorderColor = TrackCol;
        refresh.Click += (_, _) => onRefresh();

        _footer.Location = new Point(116, 214);
        _footer.Size = new Size(250, 30);

        Controls.AddRange(new Control[]
        {
            _w5Title, _w5Value, _w5Reset, _w5Track,
            _wkTitle, _wkValue, _wkReset, _wkTrack,
            refresh, _footer
        });
    }

    private void Layout5(Label title, Label value, Label reset, Panel track, int y)
    {
        title.SetBounds(16, y, 348, 18);
        value.SetBounds(16, y + 20, 348, 18);
        track.SetBounds(16, y + 44, 348, 14);
        reset.SetBounds(16, y + 62, 348, 16);
    }

    public void UpdateView(UsageSnapshot s, AppSettings cfg)
    {
        if (IsDisposed) return;

        if (!s.DataDirExists)
        {
            _w5Title.Text = "No Claude Code logs found";
            _w5Value.Text = s.DataDir;
            _w5Reset.Text = _wkTitle.Text = _wkValue.Text = _wkReset.Text = "";
            SetBar(_w5Track, _w5Fill, 0);
            SetBar(_wkTrack, _wkFill, 0);
            _footer.Text = "";
            return;
        }

        bool cr = cfg.CountCacheReadsInWindow;

        long w5used = s.CurrentWindow.Billable(cr);
        long w5cap = cfg.Window5hCapTokens;
        double f5 = (double)w5used / w5cap;
        _w5Title.Text = "5-hour window";
        _w5Value.Text = $"{Pct(f5)}    {Short(w5used)} / {Short(w5cap)} tokens";
        _w5Reset.Text = s.WindowEndsAt is { } e
            ? $"resets {e:HH:mm}  ({Remaining(e)})"
            : "idle — a fresh window is available";
        SetBar(_w5Track, _w5Fill, f5);

        long wkUsed = s.Week.Billable(cr);
        long wkCap = cfg.EffectiveWeeklyCapTokens;
        double fw = (double)wkUsed / wkCap;
        _wkTitle.Text = "This week";
        _wkValue.Text = $"{Pct(fw)}    {Short(wkUsed)} / {Short(wkCap)} tokens";
        _wkReset.Text = $"resets {s.WeekResetsAt:ddd HH:mm}  ({Remaining(s.WeekResetsAt)})";
        SetBar(_wkTrack, _wkFill, fw);

        _footer.Text = $"{s.CurrentWindow.Turns + s.Week.Turns} turns tracked · updated {s.GeneratedAt:HH:mm:ss}"
                     + (cr ? " · incl. cache reads" : "");
    }

    private static void SetBar(Panel track, Panel fill, double frac)
    {
        double f = Math.Clamp(frac, 0, 1);
        fill.Width = (int)Math.Round(track.ClientSize.Width * f);
        fill.Height = track.ClientSize.Height;
        fill.BackColor = frac >= 0.85 ? Red : frac >= 0.60 ? Amber : Green;
    }

    // ---- formatting ----

    private static string Pct(double f) => $"{Math.Min(f, 9.99) * 100:0}%";

    private static string Short(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:0.##}M";
        if (n >= 1_000) return $"{n / 1_000.0:0.#}k";
        return n.ToString();
    }

    private static string Remaining(DateTime when)
    {
        var d = when - DateTime.Now;
        if (d <= TimeSpan.Zero) return "now";
        if (d.TotalDays >= 1) return $"in {(int)d.TotalDays}d {d.Hours}h";
        if (d.TotalHours >= 1) return $"in {(int)d.TotalHours}h {d.Minutes}m";
        return $"in {d.Minutes}m";
    }

    // ---- control factories ----

    private static Label Mk(bool bold = false, bool dim = false) => new()
    {
        AutoSize = false,
        ForeColor = dim ? Dim : Fg,
        Font = new Font("Segoe UI", bold ? 10f : 9f, bold ? FontStyle.Bold : FontStyle.Regular),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Panel MkTrack() => new() { BackColor = TrackCol };
    private static Panel MkFill() => new() { BackColor = Green, Location = new Point(0, 0) };
}
