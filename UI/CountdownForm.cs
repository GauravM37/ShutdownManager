using ShutdownManager.Models;

namespace ShutdownManager.UI;

public sealed class CountdownForm : Form
{
    private readonly DateTime _deadlineUtc;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Label _countdownLabel = new();
    private readonly Label _messageLabel = new();
    private readonly ComboBox _snoozeMinutes = new();

    public CountdownForm(ScheduleItem schedule, int countdownSeconds, bool forceCloseApplications = false)
    {
        _deadlineUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(countdownSeconds, 1, 86_400));
        Text = "Scheduled operation pending";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? System.Drawing.SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = new Size(520, 340);
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.White;
        ControlBox = true;

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28), BackColor = Color.White };
        var eyebrow = new Label
        {
            Text = "SCHEDULED POWER OPERATION",
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(37, 99, 235),
            AutoSize = true,
            Location = new Point(28, 22)
        };
        var title = new Label
        {
            Text = ActionLabel(schedule.Action),
            Font = new Font("Segoe UI Semibold", 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 43, 66),
            AutoSize = true,
            Location = new Point(26, 50)
        };
        var forceWarningApplies = forceCloseApplications && (schedule.Action is PowerAction.Shutdown or PowerAction.Restart);
        _messageLabel.Text = forceWarningApplies
            ? $"Schedule: {schedule.Name}\nRunning applications will be force-closed when this countdown ends. Unsaved work may be lost."
            : $"Schedule: {schedule.Name}\nThe operation will begin when the countdown reaches zero.";
        _messageLabel.Font = new Font("Segoe UI", 9.5f);
        _messageLabel.ForeColor = Color.FromArgb(91, 105, 121);
        _messageLabel.Location = new Point(29, 98);
        _messageLabel.Size = new Size(450, forceWarningApplies ? 68 : 48);

        _countdownLabel.Font = new Font("Segoe UI Semibold", 35f, FontStyle.Bold);
        _countdownLabel.ForeColor = Color.FromArgb(37, 99, 235);
        _countdownLabel.Text = "00:00:00";
        _countdownLabel.TextAlign = ContentAlignment.MiddleCenter;
        _countdownLabel.Location = new Point(25, forceWarningApplies ? 171 : 153);
        _countdownLabel.Size = new Size(460, 65);

        _snoozeMinutes.DropDownStyle = ComboBoxStyle.DropDownList;
        _snoozeMinutes.Items.AddRange(new object[] { "5 minutes", "10 minutes", "15 minutes" });
        _snoozeMinutes.SelectedIndex = 0;
        _snoozeMinutes.Width = 115;
        _snoozeMinutes.Location = new Point(29, 257);

        var snooze = MakeButton("Snooze", Color.FromArgb(238, 242, 247), Color.FromArgb(45, 62, 82));
        snooze.Location = new Point(155, 253);
        snooze.Click += (_, _) =>
        {
            var minutes = _snoozeMinutes.SelectedIndex switch { 1 => 10, 2 => 15, _ => 5 };
            Outcome = CountdownOutcome.Snoozed(minutes);
            DialogResult = DialogResult.Retry;
            Close();
        };

        var cancel = MakeButton("Cancel operation", Color.FromArgb(220, 38, 38), Color.White);
        cancel.Location = new Point(285, 253);
        cancel.Width = 195;
        cancel.Click += (_, _) => CancelCountdown();

        panel.Controls.AddRange(new Control[] { eyebrow, title, _messageLabel, _countdownLabel, _snoozeMinutes, snooze, cancel });
        Controls.Add(panel);
        CancelButton = cancel;
        _timer.Tick += (_, _) => UpdateCountdown();
        _timer.Start();
        UpdateCountdown();
    }

    public CountdownOutcome Outcome { get; private set; } = CountdownOutcome.Cancelled();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        // Closing the window with Alt+F4 is treated as cancellation.
        if (DialogResult == DialogResult.None)
        {
            Outcome = CountdownOutcome.Cancelled();
            DialogResult = DialogResult.Cancel;
        }
        base.OnFormClosing(e);
    }

    private void UpdateCountdown()
    {
        var remaining = _deadlineUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            Outcome = CountdownOutcome.Executed();
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        var totalSeconds = (long)Math.Ceiling(remaining.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        _countdownLabel.Text = $"{hours:00}:{minutes:00}:{seconds:00}";
    }

    private void CancelCountdown()
    {
        _timer.Stop();
        Outcome = CountdownOutcome.Cancelled();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private static Button MakeButton(string text, Color backColor, Color foreColor) => new()
    {
        Text = text,
        Width = 115,
        Height = 38,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = foreColor,
        Cursor = Cursors.Hand,
        Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
    };

    private static string ActionLabel(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Shutting down",
        PowerAction.Restart => "Restarting Windows",
        PowerAction.Sleep => "Entering sleep mode",
        PowerAction.Hibernate => "Hibernating Windows",
        PowerAction.LogOff => "Signing out",
        PowerAction.Lock => "Locking computer",
        _ => "Power operation"
    };
}
