using ShutdownManager.Models;

namespace ShutdownManager.UI;

public sealed class ScheduleEditorForm : Form
{
    private readonly ScheduleItem? _original;
    private readonly TextBox _name = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _action = new();
    private readonly DateTimePicker _time = new();
    private readonly DateTimePicker _date = new();
    private readonly CheckBox _enabled = new();
    private readonly Label _dateLabel = new();
    private readonly Label _weekdaysLabel = new();
    private readonly Dictionary<DayOfWeek, CheckBox> _weekdayChecks = new();

    private TableLayoutPanel? _rootLayout;
    private FlowLayoutPanel? _datePanel;
    private TableLayoutPanel? _weekdaysPanel;

    public ScheduleEditorForm(ScheduleItem? schedule = null)
    {
        _original = schedule?.Clone();
        Text = schedule is null ? "Create schedule" : "Edit schedule";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? System.Drawing.SystemIcons.Application;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(920, 410);
        MinimumSize = new Size(936, 449);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;

        BuildLayout();
        // Load from the same snapshot retained for SaveSchedule. This guarantees
        // the editor opens in edit mode with the selected schedule's actual values.
        LoadSchedule(_original);
    }

    public ScheduleItem? ResultSchedule { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 2,
            RowCount = 8,
            BackColor = Color.White
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Keep the optional date and weekday rows collapsed until their mode is selected.
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout = root;

        var heading = new Label
        {
            Text = _original is null ? "Create a schedule" : "Edit schedule",
            Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 43, 66),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);

        AddLabelAndControl(root, "Schedule name", _name, 1);
        _name.Dock = DockStyle.Fill;
        _name.PlaceholderText = "For example: Weekday evening";

        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        // Use the ComboBox Items collection instead of DataSource binding. This
        // guarantees items are available synchronously when LoadSchedule selects
        // the saved value (DataSource binding could still expose an empty list
        // during form construction on some WinForms startup paths).
        _type.Items.AddRange(Enum.GetValues<ScheduleType>().Cast<object>().ToArray());
        _type.Dock = DockStyle.Fill;
        _type.SelectedIndexChanged += (_, _) => UpdateTypeVisibility();
        AddLabelAndControl(root, "Repeat", _type, 2);

        _action.DropDownStyle = ComboBoxStyle.DropDownList;
        _action.Items.AddRange(Enum.GetValues<PowerAction>().Cast<object>().ToArray());
        _action.Dock = DockStyle.Fill;
        AddLabelAndControl(root, "Power operation", _action, 3);

        var timePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 0)
        };
        _time.Format = DateTimePickerFormat.Time;
        _time.ShowUpDown = true;
        _time.Width = 145;
        timePanel.Controls.Add(_time);
        timePanel.Controls.Add(new Label
        {
            Text = "Local computer time",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(8, 7, 0, 0)
        });
        AddLabelAndControl(root, "Time", timePanel, 4);

        _datePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 0, 0)
        };
        _date.Format = DateTimePickerFormat.Custom;
        _date.CustomFormat = "dddd, dd MMMM yyyy";
        _date.Width = 230;
        _date.MinDate = DateTime.Today;
        _datePanel.Controls.Add(_date);
        AddLabelAndControl(root, "Date (one-time)", _datePanel, 5, _dateLabel);

        _weekdaysPanel = BuildWeekdayPicker();
        AddLabelAndControl(root, "Days (weekly)", _weekdaysPanel, 6, _weekdaysLabel);

        _enabled.Text = "Schedule enabled";
        _enabled.AutoSize = true;
        _enabled.Checked = true;
        _enabled.ForeColor = Color.FromArgb(45, 62, 82);
        _enabled.Anchor = AnchorStyles.Left;
        root.Controls.Add(_enabled, 1, 7);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.White
        };
        var save = CreateButton("Save schedule", Color.FromArgb(37, 99, 235), Color.White);
        var cancel = CreateButton("Cancel", Color.FromArgb(238, 242, 247), Color.FromArgb(45, 62, 82));
        save.Click += (_, _) => SaveSchedule();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        Controls.Add(root);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private TableLayoutPanel BuildWeekdayPicker()
    {
        var picker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 2,
            Margin = new Padding(0, 3, 0, 0),
            Padding = new Padding(4, 0, 4, 0),
            BackColor = Color.White,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        picker.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        picker.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        foreach (var day in OrderedWeekdays())
        {
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7f));

            var dayLabel = new Label
            {
                Text = day.ToString(),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomCenter,
                AutoEllipsis = false,
                AutoSize = false,
                UseMnemonic = false,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(76, 91, 110),
                Margin = new Padding(5, 0, 5, 1)
            };

            var check = new CheckBox
            {
                AutoSize = true,
                Anchor = AnchorStyles.None,
                Margin = Padding.Empty,
                Cursor = Cursors.Hand,
                AccessibleName = day.ToString(),
                AccessibleDescription = $"Include {day} in this weekly schedule"
            };
            _weekdayChecks.Add(day, check);

            var column = _weekdayChecks.Count - 1;
            picker.Controls.Add(dayLabel, column, 0);
            picker.Controls.Add(check, column, 1);
        }

        return picker;
    }

    private static IEnumerable<DayOfWeek> OrderedWeekdays()
    {
        return new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };
    }

    private void AddLabelAndControl(TableLayoutPanel panel, string labelText, Control control, int row, Label? labelReference = null)
    {
        var label = labelReference ?? new Label();
        label.Text = labelText;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.ForeColor = Color.FromArgb(76, 91, 110);
        label.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        panel.Controls.Add(label, 0, row);
        control.Margin = new Padding(0, 6, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private static Button CreateButton(string text, Color back, Color fore)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Padding = new Padding(14, 0, 14, 0),
            Cursor = Cursors.Hand
        };
    }

    private void LoadSchedule(ScheduleItem? schedule)
    {
        // Reset the controls first so this method remains correct if it is reused.
        _name.Clear();
        _enabled.Checked = true;
        foreach (var check in _weekdayChecks.Values)
            check.Checked = false;

        if (schedule is null)
        {
            // Start new schedules at the next usable minute instead of a fixed
            // default. The picker stores whole minutes, so round up when the
            // current time includes seconds to keep the default safely in future.
            var defaultTime = DateTime.Now.AddMinutes(1);
            if (defaultTime.Second > 0 || defaultTime.Millisecond > 0)
                defaultTime = defaultTime.AddMinutes(1);
            defaultTime = new DateTime(
                defaultTime.Year, defaultTime.Month, defaultTime.Day,
                defaultTime.Hour, defaultTime.Minute, 0);

            _time.Value = defaultTime;
            _date.Value = DateTime.Today;
            _type.SelectedItem = ScheduleType.Daily;
            _action.SelectedItem = PowerAction.Shutdown;
        }
        else
        {
            _name.Text = schedule.Name;

            // Select from the already populated Items collection. Avoid assigning
            // SelectedIndex based on a separate enum array: that assumes the
            // ComboBox's bound list has finished populating, which caused the
            // reported ArgumentOutOfRangeException during Edit.
            SelectComboValue(_type, schedule.Type, ScheduleType.Daily);
            SelectComboValue(_action, schedule.Action, PowerAction.Shutdown);

            var savedOneTime = schedule.OneTimeDateTimeLocal;
            var savedTime = schedule.Type == ScheduleType.OneTime && savedOneTime.HasValue
                ? savedOneTime.Value.TimeOfDay
                : schedule.TimeOfDay.ToTimeSpan();
            var timeValue = DateTime.Today.Add(savedTime);
            _time.Value = timeValue < _time.MinDate ? _time.MinDate :
                timeValue > _time.MaxDate ? _time.MaxDate : timeValue;

            if (schedule.Type == ScheduleType.OneTime && savedOneTime is DateTime savedDateTime)
            {
                // Existing expired one-time schedules should still display their
                // saved date when edited; SaveSchedule will still require a future
                // date/time before accepting changes.
                var savedDate = savedDateTime.Date;
                if (savedDate < _date.MinDate)
                    _date.MinDate = savedDate;
                _date.Value = savedDate;
            }
            else
            {
                _date.Value = DateTime.Today.AddDays(1);
            }

            _enabled.Checked = schedule.IsEnabled;

            foreach (var day in schedule.Weekdays ?? new List<DayOfWeek>())
            {
                if (_weekdayChecks.TryGetValue(day, out var check))
                    check.Checked = true;
            }
        }

        // Apply visibility only after the selected repeat type and all values
        // have been restored. This also ensures Edit opens with the right fields.
        UpdateTypeVisibility();
    }

    private static void SelectComboValue<TEnum>(ComboBox combo, TEnum value, TEnum fallback)
        where TEnum : struct, Enum
    {
        // The enum values are inserted into Items in Enum.GetValues order in
        // BuildLayout. Resolve against that same ordered list, then validate the
        // index against the actual ComboBox collection before selecting it.
        var values = Enum.GetValues<TEnum>();
        var requestedValue = values.Contains(value) ? value : fallback;
        var selectedIndex = Array.IndexOf(values, requestedValue);

        if (selectedIndex < 0 || selectedIndex >= combo.Items.Count)
        {
            throw new InvalidOperationException(
                $"Could not select {typeof(TEnum).Name}.{requestedValue}: " +
                $"the dropdown contains {combo.Items.Count} item(s), expected at least {selectedIndex + 1}.");
        }

        combo.SelectedIndex = selectedIndex;
    }

    private void UpdateTypeVisibility()
    {
        var type = _type.SelectedItem is ScheduleType selected ? selected : ScheduleType.Daily;
        var showDate = type == ScheduleType.OneTime;
        var showWeekdays = type == ScheduleType.Weekly;

        _dateLabel.Visible = showDate;
        if (_datePanel is not null)
            _datePanel.Visible = showDate;

        _weekdaysLabel.Visible = showWeekdays;
        if (_weekdaysPanel is not null)
            _weekdaysPanel.Visible = showWeekdays;

        if (_rootLayout is not null)
        {
            _rootLayout.RowStyles[5].SizeType = SizeType.Absolute;
            _rootLayout.RowStyles[5].Height = showDate ? 50 : 0;
            _rootLayout.RowStyles[6].SizeType = SizeType.Absolute;
            _rootLayout.RowStyles[6].Height = showWeekdays ? 82 : 0;
        }

        ClientSize = new Size(920, showWeekdays ? 492 : showDate ? 460 : 410);
    }

    private void SaveSchedule()
    {
        var name = _name.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Enter a name for this schedule.", "Schedule name required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _name.Focus();
            return;
        }

        var type = _type.SelectedItem is ScheduleType selectedType ? selectedType : ScheduleType.Daily;
        var selectedDays = _weekdayChecks
            .Where(pair => pair.Value.Checked)
            .Select(pair => pair.Key)
            .ToList();

        if (type == ScheduleType.Weekly && selectedDays.Count == 0)
        {
            MessageBox.Show(this, "Select at least one weekday for a weekly schedule.", "Select weekdays", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _weekdaysPanel?.Focus();
            return;
        }

        var time = TimeOnly.FromDateTime(_time.Value);
        DateTime? oneTime = null;
        if (type == ScheduleType.OneTime)
        {
            oneTime = DateTime.SpecifyKind(_date.Value.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
            if (oneTime.Value <= DateTime.Now)
            {
                MessageBox.Show(this, "Choose a future date and time for a one-time schedule.", "Date/time must be in the future", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _date.Focus();
                return;
            }
        }

        var result = _original?.Clone() ?? new ScheduleItem();
        result.Name = name;
        result.Type = type;
        result.Action = _action.SelectedItem is PowerAction action ? action : PowerAction.Shutdown;
        result.TimeOfDay = time;
        result.Weekdays = type == ScheduleType.Weekly ? selectedDays : new List<DayOfWeek>();
        result.OneTimeDateTimeLocal = oneTime;
        result.IsEnabled = _enabled.Checked;
        ResultSchedule = result;
        DialogResult = DialogResult.OK;
        Close();
    }
}
