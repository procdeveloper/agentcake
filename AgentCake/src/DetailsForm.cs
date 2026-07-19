using System.Drawing;

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