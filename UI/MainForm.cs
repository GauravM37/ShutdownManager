using ShutdownManager.Models;
using ShutdownManager.Services;
using System.Diagnostics;

namespace ShutdownManager.UI;

public sealed class MainForm : Form
{
    private readonly AppRepository _repository;
    private readonly PowerActionService _powerActions;
    private readonly StartupManager _startupManager;
    private readonly Logger _logger;
    // When enabled, suppress WinForms' first automatic Show() so the window never flashes.
    private readonly bool _hideOnInitialShow;
    private bool _initialShowRequestHandled;

    private readonly TabControl _tabs = new();
    private readonly List<Button> _navigationButtons = new();
    private readonly DataGridView _scheduleGrid = new();
    // Retain the last clicked schedule ID because toolbar buttons take focus away
    // from the DataGridView and SelectedRows can be empty during the button event.
    private Guid? _lastSelectedScheduleId;
    private readonly DataGridView _historyGrid = new();
    private readonly Label _nextOperationValue = new();
    private readonly Label _nextTimeValue = new();
    private readonly Label _remainingValue = new();
    private readonly Label _scheduleStatusValue = new();
    private readonly Button _quickToggleButton = new();
    private readonly Label _dashboardSummary = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _startMinimized = new();
    private readonly CheckBox _minimizeToTray = new();
    private readonly CheckBox _showNotifications = new();
    private readonly CheckBox _forceCloseApplications = new();
    private readonly NumericUpDown _countdownValue = new();
    private readonly ComboBox _countdownUnit = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 1000 };
    private FlowLayoutPanel? _powerCardsPanel;
    private bool? _lastKnownHibernateAvailability;

    private SchedulerService? _scheduler;
    private bool _allowExit;
    private bool _exiting;

    public MainForm(
        AppRepository repository,
        PowerActionService powerActions,
        StartupManager startupManager,
        Logger logger,
        bool startMinimizedArgument)
    {
        _repository = repository;
        _powerActions = powerActions;
        _startupManager = startupManager;
        _logger = logger;
        _hideOnInitialShow = startMinimizedArgument || repository.GetSnapshot().Settings.StartMinimized;

        Text = "Shutdown Manager";
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                ?? System.Drawing.SystemIcons.Application;
        }
        catch (Exception exception)
        {
            _logger.Warning($"Could not load the embedded application icon: {exception.Message}");
            Icon = System.Drawing.SystemIcons.Application;
        }
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(950, 650);
        Size = new Size(1180, 780);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(245, 247, 251);

        BuildInterface();
        ConfigureTrayIcon();
        LoadSettingsIntoControls();
        RefreshAllViews();

        _scheduler = new SchedulerService(_repository, _logger, HandleScheduledTriggerAsync);
        _scheduler.Start();

        // The timer is only for live dashboard values. Hibernate availability is
        // intentionally refreshed when the user opens Power Options, not every second.
        _uiTimer.Tick += (_, _) => RefreshDashboard();
        _uiTimer.Start();

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                WindowState = FormWindowState.Normal;
            }
        };
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(245, 247, 251)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 37, 58),
            Padding = new Padding(22, 7, 22, 7)
        };
        // Use fixed label bounds and a taller header to prevent font ascenders/descenders
        // from overlapping or being clipped at different Windows display scales.
        var title = new Label
        {
            Text = "Shutdown Manager",
            AutoSize = false,
            Size = new Size(520, 34),
            Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true,
            Location = new Point(2, 1)
        };
        var subtitle = new Label
        {
            Text = "Plan power operations and let Windows handle the rest",
            AutoSize = false,
            Size = new Size(700, 24),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(186, 199, 216),
            TextAlign = ContentAlignment.MiddleLeft,
            UseCompatibleTextRendering = true,
            Location = new Point(3, 43)
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(245, 247, 251) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var navigation = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 47, 70), Padding = new Padding(12, 20, 12, 12) };
        var navLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty
        };
        AddNavigationButton(navLayout, "Dashboard", 0);
        AddNavigationButton(navLayout, "Schedules", 1);
        AddNavigationButton(navLayout, "Power options", 2);
        AddNavigationButton(navLayout, "Settings", 3);
        AddNavigationButton(navLayout, "Event history", 4);
        navigation.Controls.Add(navLayout);
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = Color.Transparent
        };
        var version = new Label
        {
            Text = "VERSION 1.0.0",
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = Color.FromArgb(144, 162, 184),
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var githubMark = new PictureBox
        {
            Location = new Point(0, 26),
            Size = new Size(19, 19),
            BackColor = Color.White,
            SizeMode = PictureBoxSizeMode.Zoom,
            Cursor = Cursors.Hand,
            AccessibleName = "GitHub profile"
        };
        githubMark.Image = LoadGitHubMark();
        githubMark.Click += (_, _) => OpenGitHubProfile();

        var githubLink = new LinkLabel
        {
            Text = "GauravM37",
            AutoSize = true,
            Location = new Point(26, 26),
            LinkColor = Color.FromArgb(196, 211, 230),
            ActiveLinkColor = Color.White,
            VisitedLinkColor = Color.FromArgb(196, 211, 230),
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        githubLink.LinkClicked += (_, _) => OpenGitHubProfile();
        footer.Controls.Add(version);
        footer.Controls.Add(githubMark);
        footer.Controls.Add(githubLink);
        navigation.Controls.Add(footer);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Appearance = TabAppearance.FlatButtons;
        _tabs.ItemSize = new Size(0, 1);
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.Multiline = true;
        _tabs.TabPages.Add(CreateDashboardPage());
        _tabs.TabPages.Add(CreateSchedulesPage());
        _tabs.TabPages.Add(CreatePowerOptionsPage());
        _tabs.TabPages.Add(CreateSettingsPage());
        _tabs.TabPages.Add(CreateHistoryPage());
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateNavigationSelection();
            if (_tabs.SelectedIndex == 2)
                RefreshPowerOptionsCards(force: true);
        };
        UpdateNavigationSelection();

        body.Controls.Add(navigation, 0, 0);
        body.Controls.Add(_tabs, 1, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        Controls.Add(root);
    }

    private void AddNavigationButton(FlowLayoutPanel parent, string text, int tabIndex)
    {
        var button = new Button
        {
            Text = text,
            Tag = tabIndex,
            Width = 178,
            Height = 46,
            Margin = new Padding(0, 0, 0, 10),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(13, 0, 8, 0),
            BackColor = Color.FromArgb(30, 47, 70),
            ForeColor = Color.FromArgb(224, 232, 242),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 64, 91);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(55, 96, 148);
        button.Click += (_, _) => SelectTab(tabIndex);
        _navigationButtons.Add(button);
        parent.Controls.Add(button);
    }

    private void SelectTab(int index)
    {
        if (index >= 0 && index < _tabs.TabPages.Count)
            _tabs.SelectedIndex = index;
    }

    private void UpdateNavigationSelection()
    {
        var activeColor = Color.FromArgb(48, 91, 145);
        var inactiveColor = Color.FromArgb(30, 47, 70);
        var activeTextColor = Color.White;
        var inactiveTextColor = Color.FromArgb(224, 232, 242);

        foreach (var button in _navigationButtons)
        {
            var isSelected = button.Tag is int index && index == _tabs.SelectedIndex;
            button.BackColor = isSelected ? activeColor : inactiveColor;
            button.ForeColor = isSelected ? activeTextColor : inactiveTextColor;
            button.AccessibleDescription = isSelected ? "Selected navigation page" : null;
        }
    }

    private TabPage CreateDashboardPage()
    {
        var page = CreatePage("Dashboard");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(245, 247, 251) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var headingPanel = new Panel { Dock = DockStyle.Fill };
        headingPanel.Controls.Add(new Label
        {
            Text = "Overview",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 43, 66),
            Location = new Point(0, 0)
        });
        _quickToggleButton.Text = "Disable all schedules";
        _quickToggleButton.Size = new Size(180, 36);
        _quickToggleButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _quickToggleButton.Location = new Point(690, 5);
        StylePrimaryButton(_quickToggleButton);
        _quickToggleButton.Click += (_, _) => ToggleAllSchedules();
        headingPanel.Controls.Add(_quickToggleButton);
        headingPanel.Resize += (_, _) => _quickToggleButton.Left = headingPanel.ClientSize.Width - _quickToggleButton.Width;

        // Four equal-width dashboard cards: each card receives one quarter of the
        // available width instead of relying on fixed-width FlowLayoutPanel items.
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0, 2, 0, 4),
            BackColor = Color.Transparent,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        for (var i = 0; i < 4; i++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var metricCards = new[]
        {
            CreateMetricCard("NEXT OPERATION", _nextOperationValue, "Action selected for the next occurrence"),
            CreateMetricCard("NEXT EXECUTION", _nextTimeValue, "Local date and time"),
            CreateMetricCard("TIME REMAINING", _remainingValue, "Updates automatically"),
            CreateMetricCard("SCHEDULE STATUS", _scheduleStatusValue, "Background monitoring state")
        };
        for (var i = 0; i < metricCards.Length; i++)
        {
            metricCards[i].Dock = DockStyle.Fill;
            metricCards[i].Margin = new Padding(0, 0, i == metricCards.Length - 1 ? 0 : 12, 0);
            cards.Controls.Add(metricCards[i], i, 0);
        }

        var lower = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16) };
        lower.Controls.Add(new Label
        {
            Text = "Upcoming schedules",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 43, 66),
            Location = new Point(16, 12)
        });
        _dashboardSummary.Text = "Schedules are checked automatically in the background.";
        _dashboardSummary.AutoSize = true;
        _dashboardSummary.ForeColor = Color.FromArgb(105, 119, 137);
        _dashboardSummary.Location = new Point(17, 40);
        lower.Controls.Add(_dashboardSummary);
        var dashboardGrid = CreateReadOnlyGrid();
        dashboardGrid.Location = new Point(16, 72);
        dashboardGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dashboardGrid.Height = 330;
        dashboardGrid.Width = 800;
        dashboardGrid.Columns.Add(MakeTextColumn("name", "Schedule", 220));
        dashboardGrid.Columns.Add(MakeTextColumn("action", "Operation", 140));
        dashboardGrid.Columns.Add(MakeTextColumn("next", "Next occurrence", 230));
        dashboardGrid.Columns.Add(MakeTextColumn("status", "Status", 110));
        lower.Controls.Add(dashboardGrid);
        _dashboardGrid = dashboardGrid;
        lower.Resize += (_, _) =>
        {
            dashboardGrid.Width = lower.ClientSize.Width - 32;
            dashboardGrid.Height = lower.ClientSize.Height - 88;
        };

        layout.Controls.Add(headingPanel, 0, 0);
        layout.Controls.Add(cards, 0, 1);
        layout.Controls.Add(lower, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private DataGridView _dashboardGrid = new();

    private static Panel CreateMetricCard(string heading, Label value, string description)
    {
        var card = new Panel
        {
            Width = 205,
            Height = 132,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(14)
        };
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
        var title = new Label
        {
            Text = heading,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(119, 133, 151),
            Location = new Point(14, 13)
        };
        value.Text = "—";
        value.AutoSize = false;
        value.Location = new Point(14, 38);
        value.Size = new Size(176, 42);
        value.Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold);
        value.ForeColor = Color.FromArgb(28, 48, 73);
        value.AutoEllipsis = true;
        var desc = new Label
        {
            Text = description,
            AutoSize = false,
            Location = new Point(14, 89),
            Size = new Size(177, 30),
            ForeColor = Color.FromArgb(119, 133, 151),
            Font = new Font("Segoe UI", 8f)
        };
        card.Controls.AddRange(new Control[] { title, value, desc });
        // Keep the value and description aligned to the card's responsive width.
        card.Resize += (_, _) =>
        {
            var contentWidth = Math.Max(80, card.ClientSize.Width - 28);
            value.Width = contentWidth;
            desc.Width = contentWidth;
        };
        return card;
    }

    private TabPage CreateSchedulesPage()
    {
        var page = CreatePage("Schedule management");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(245, 247, 251) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Label { Text = "Schedule management", AutoSize = true, Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 43, 66) };
        layout.Controls.Add(heading, 0, 0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var add = MakeToolbarButton("+ Add schedule", primary: true);
        var edit = MakeToolbarButton("Edit", primary: false);
        var toggle = MakeToolbarButton("Enable / disable", primary: false);
        var delete = MakeToolbarButton("Delete", primary: false);
        add.Click += (_, _) => AddSchedule();
        edit.Click += (_, _) => EditSelectedSchedule();
        toggle.Click += (_, _) => ToggleSelectedSchedule();
        delete.Click += (_, _) => DeleteSelectedSchedule();
        toolbar.Controls.AddRange(new Control[] { add, edit, toggle, delete });
        layout.Controls.Add(toolbar, 0, 1);

        ConfigureScheduleGrid(_scheduleGrid);
        layout.Controls.Add(_scheduleGrid, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreatePowerOptionsPage()
    {
        var page = CreatePage("Power options");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(245, 247, 251) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Power options", AutoSize = true, Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 43, 66) }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Immediate operations require confirmation. Scheduled operations use the countdown and can be cancelled or snoozed.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(91, 105, 121),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);

        _powerCardsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            Padding = new Padding(0, 10, 0, 0)
        };
        layout.Controls.Add(_powerCardsPanel, 0, 2);
        page.Controls.Add(layout);
        // The SelectedIndexChanged handler refreshes availability when this page is opened.
        return page;
    }

    private void RefreshPowerOptionsCards(bool force = false)
    {
        if (_powerCardsPanel is null || _powerCardsPanel.IsDisposed)
            return;

        var hibernateAvailable = _powerActions.IsHibernateAvailable;
        if (!force && _lastKnownHibernateAvailability == hibernateAvailable)
            return;

        _lastKnownHibernateAvailability = hibernateAvailable;
        _powerCardsPanel.SuspendLayout();
        try
        {
            foreach (Control oldCard in _powerCardsPanel.Controls)
                oldCard.Dispose();
            _powerCardsPanel.Controls.Clear();
            AddPowerCard(_powerCardsPanel, "Shut down", "Close Windows and power off the computer.", PowerAction.Shutdown);
            AddPowerCard(_powerCardsPanel, "Restart", "Close Windows and start it again.", PowerAction.Restart);
            AddPowerCard(_powerCardsPanel, "Sleep", "Put the computer into a low-power sleep state.", PowerAction.Sleep);
            AddPowerCard(_powerCardsPanel, "Hibernate", "Save the session and power off where supported.", PowerAction.Hibernate);
            AddPowerCard(_powerCardsPanel, "Log off", "Sign out of the current Windows account.", PowerAction.LogOff);
            AddPowerCard(_powerCardsPanel, "Lock", "Lock the current Windows session.", PowerAction.Lock);
        }
        finally
        {
            _powerCardsPanel.ResumeLayout(performLayout: true);
        }

        _logger.Info($"Power Options refreshed. Hibernate available={hibernateAvailable}.");
    }

    private void AddPowerCard(FlowLayoutPanel parent, string title, string description, PowerAction action)
    {
        var card = new Panel { Width = 270, Height = 165, BackColor = Color.White, Margin = new Padding(0, 0, 14, 14), Padding = new Padding(16) };
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Color.FromArgb(226, 232, 240), ButtonBorderStyle.Solid);
        card.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(16, 14), Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 43, 66) });
        var hibernateUnavailable = action == PowerAction.Hibernate && _lastKnownHibernateAvailability == false;
        var descriptionText = hibernateUnavailable
            ? "Unavailable: Windows hibernation is disabled or unsupported. Check 'powercfg /a'."
            : description;
        card.Controls.Add(new Label { Text = descriptionText, Location = new Point(16, 47), Size = new Size(235, 45), ForeColor = Color.FromArgb(105, 119, 137), Font = new Font("Segoe UI", 8.5f) });
        var button = MakeToolbarButton(hibernateUnavailable ? "Unavailable" : "Run now", primary: !hibernateUnavailable);
        button.Location = new Point(16, 108);
        button.Enabled = !hibernateUnavailable;
        if (!hibernateUnavailable)
            button.Click += async (_, _) => await ConfirmAndExecuteImmediateAsync(action);
        card.Controls.Add(button);
        parent.Controls.Add(card);
    }

    private TabPage CreateSettingsPage()
    {
        var page = CreatePage("Settings");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(245, 247, 251) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(new Label { Text = "Settings", AutoSize = true, Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 43, 66) }, 0, 0);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 4, 0, 0) };
        var startupGroup = MakeSettingsGroup("Windows startup", 130);
        _startWithWindows.Text = "Start Shutdown Manager when I sign in to Windows";
        _startWithWindows.AutoSize = true;
        _startWithWindows.Location = new Point(18, 42);
        _startMinimized.Text = "Start minimized to the system tray";
        _startMinimized.AutoSize = true;
        _startMinimized.Location = new Point(18, 76);
        startupGroup.Controls.AddRange(new Control[] { _startWithWindows, _startMinimized });

        var behaviorGroup = MakeSettingsGroup("Application behavior", 166);
        _minimizeToTray.Text = "When I close the window, keep the app running in the system tray";
        _minimizeToTray.AutoSize = true;
        _minimizeToTray.Location = new Point(18, 44);
        _showNotifications.Text = "Show Windows notification-area alerts before scheduled operations";
        _showNotifications.AutoSize = true;
        _showNotifications.Location = new Point(18, 78);
        _forceCloseApplications.Text = "Force-close running applications during Shutdown / Restart (unsaved data may be lost)";
        _forceCloseApplications.AutoSize = true;
        _forceCloseApplications.Location = new Point(18, 112);
        behaviorGroup.Controls.AddRange(new Control[] { _minimizeToTray, _showNotifications, _forceCloseApplications });

        var countdownGroup = MakeSettingsGroup("Countdown before an operation", 145);
        var durationLabel = new Label { Text = "Countdown duration", AutoSize = true, Location = new Point(18, 45), ForeColor = Color.FromArgb(76, 91, 110) };
        _countdownValue.Minimum = 1;
        _countdownValue.Maximum = 86_400;
        _countdownValue.Width = 115;
        _countdownValue.Location = new Point(185, 40);
        _countdownUnit.DropDownStyle = ComboBoxStyle.DropDownList;
        _countdownUnit.Items.AddRange(new object[] { "seconds", "minutes" });
        _countdownUnit.SelectedIndex = 0;
        _countdownUnit.Width = 105;
        _countdownUnit.Location = new Point(310, 40);
        var helper = new Label { Text = "Choose any duration from 1 second up to 24 hours. Cancel and snooze remain available during the countdown.", AutoSize = true, Location = new Point(18, 83), ForeColor = Color.FromArgb(105, 119, 137) };
        countdownGroup.Controls.AddRange(new Control[] { durationLabel, _countdownValue, _countdownUnit, helper });

        options.Controls.Add(startupGroup);
        options.Controls.Add(behaviorGroup);
        options.Controls.Add(countdownGroup);
        layout.Controls.Add(options, 0, 1);

        var footer = new Panel { Dock = DockStyle.Fill };
        var save = MakeToolbarButton("Save settings", primary: true);
        save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        save.Location = new Point(650, 6);
        save.Click += (_, _) => SaveSettings();
        footer.Controls.Add(save);
        footer.Resize += (_, _) => save.Left = footer.ClientSize.Width - save.Width;
        layout.Controls.Add(footer, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage CreateHistoryPage()
    {
        var page = CreatePage("Shutdown history");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 2, BackColor = Color.FromArgb(245, 247, 251) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Event history", AutoSize = true, Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold), ForeColor = Color.FromArgb(25, 43, 66) }, 0, 0);
        _historyGrid.Dock = DockStyle.Fill;
        _historyGrid.AllowUserToAddRows = false;
        _historyGrid.AllowUserToDeleteRows = false;
        _historyGrid.ReadOnly = true;
        _historyGrid.RowHeadersVisible = false;
        _historyGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _historyGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _historyGrid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _historyGrid.Columns.Add(MakeTextColumn("time", "Date / time", 155));
        _historyGrid.Columns.Add(MakeTextColumn("schedule", "Schedule", 180));
        _historyGrid.Columns.Add(MakeTextColumn("action", "Operation", 110));
        _historyGrid.Columns.Add(MakeTextColumn("result", "Result", 125));
        _historyGrid.Columns.Add(MakeTextColumn("details", "Details", 330));
        layout.Controls.Add(_historyGrid, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private static TabPage CreatePage(string title) => new(title) { BackColor = Color.FromArgb(245, 247, 251), Padding = new Padding(0) };

    private static GroupBox MakeSettingsGroup(string title, int height)
    {
        return new GroupBox
        {
            Text = title,
            Width = 830,
            Height = height,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(12),
            ForeColor = Color.FromArgb(47, 65, 87),
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
        };
    }

    private static DataGridView CreateReadOnlyGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 36,
            RowTemplate = { Height = 34 }
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 243, 248);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(76, 91, 110);
        // Keep column headers visually static when a data row is selected.
        // Selection is applied to rows only; headers must not inherit the active selection color.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 243, 248);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(76, 91, 110);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Color.FromArgb(45, 62, 82);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 236, 250);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 43, 66);
        grid.GridColor = Color.FromArgb(235, 239, 244);
        return grid;
    }

    private static DataGridViewTextBoxColumn MakeTextColumn(string name, string header, int width) => new()
    {
        Name = name,
        HeaderText = header,
        Width = width,
        MinimumWidth = 80,
        SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private void ConfigureScheduleGrid(DataGridView grid)
    {
        // Capture the row under the pointer before the toolbar button takes focus.
        // This is more reliable than depending only on SelectedRows at edit time.
        grid.CellMouseDown += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count &&
                grid.Rows[e.RowIndex].Tag is ScheduleItem clickedSchedule)
            {
                _lastSelectedScheduleId = clickedSchedule.Id;
            }
        };
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count &&
                grid.Rows[e.RowIndex].Tag is ScheduleItem clickedSchedule)
            {
                _lastSelectedScheduleId = clickedSchedule.Id;
            }
        };
        grid.SelectionChanged += (_, _) =>
        {
            var selectedRow = grid.SelectedRows.Count > 0
                ? grid.SelectedRows[0]
                : grid.CurrentRow;
            if (selectedRow?.Tag is ScheduleItem selectedSchedule)
                _lastSelectedScheduleId = selectedSchedule.Id;
        };

        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(239, 243, 248);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(76, 91, 110);
        // Keep column headers visually static when a data row is selected.
        // Selection is applied to rows only; headers must not inherit the active selection color.
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 243, 248);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(76, 91, 110);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Color.FromArgb(45, 62, 82);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 236, 250);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(25, 43, 66);
        grid.GridColor = Color.FromArgb(235, 239, 244);
        grid.Columns.Add(MakeTextColumn("name", "Schedule", 210));
        grid.Columns.Add(MakeTextColumn("type", "Repeat", 115));
        grid.Columns.Add(MakeTextColumn("action", "Power operation", 140));
        grid.Columns.Add(MakeTextColumn("when", "Time / date", 260));
        grid.Columns.Add(MakeTextColumn("status", "Status", 110));
    }

    private static Button MakeToolbarButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(45, 62, 82),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Padding = new Padding(12, 0, 12, 0),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(213, 221, 231);
        return button;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Color.FromArgb(37, 99, 235);
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static Image LoadGitHubMark()
    {
        const string resourceName = "ShutdownManager.Assets.github-mark.png";
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded image resource '{resourceName}' was not found.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private void OpenGitHubProfile()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/GauravM37")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the GitHub profile link.", exception);
            MessageBox.Show(
                this,
                "The GitHub profile could not be opened. Please visit https://github.com/GauravM37 manually.",
                "Unable to open link",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ConfigureTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open application", null, (_, _) => ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());

        _notifyIcon.Icon = (System.Drawing.Icon)Icon.Clone();
        _notifyIcon.Text = "Shutdown Manager";
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.MouseClick += (_, e) =>
        {
            // Open on the first left-click; keep the right-click context menu available.
            if (e.Button == MouseButtons.Left)
                ShowFromTray();
        };
        _notifyIcon.Visible = true;
    }

    private void LoadSettingsIntoControls()
    {
        var config = _repository.GetSnapshot();
        _startWithWindows.Checked = _startupManager.IsEnabled();
        _startMinimized.Checked = config.Settings.StartMinimized;
        _minimizeToTray.Checked = config.Settings.MinimizeToTrayOnClose;
        _showNotifications.Checked = config.Settings.ShowNotifications;
        _forceCloseApplications.Checked = config.Settings.ForceCloseApplications;

        var seconds = Math.Clamp(config.Settings.CountdownSeconds, 1, 86_400);
        if (seconds % 60 == 0 && seconds >= 60)
        {
            _countdownValue.Value = seconds / 60;
            _countdownUnit.SelectedItem = "minutes";
        }
        else
        {
            _countdownValue.Value = seconds;
            _countdownUnit.SelectedItem = "seconds";
        }
    }

    private void SaveSettings()
    {
        try
        {
            var config = _repository.GetSnapshot();
            var desiredStartup = _startWithWindows.Checked;
            var startupCurrentlyEnabled = _startupManager.IsEnabled();
            if (desiredStartup)
                _startupManager.SetEnabled(true, _startMinimized.Checked);
            else if (startupCurrentlyEnabled)
                _startupManager.SetEnabled(false);

            var seconds = decimal.ToInt32(_countdownValue.Value);
            if (Equals(_countdownUnit.SelectedItem, "minutes"))
                seconds *= 60;

            config.Settings.StartWithWindows = desiredStartup;
            config.Settings.StartMinimized = _startMinimized.Checked;
            config.Settings.MinimizeToTrayOnClose = _minimizeToTray.Checked;
            config.Settings.ShowNotifications = _showNotifications.Checked;

            if (_forceCloseApplications.Checked && !config.Settings.ForceCloseApplications)
            {
                var enableForceClose = MessageBox.Show(
                    this,
                    "When enabled, Shutdown Manager will ask Windows to force-close running applications during Shutdown and Restart without giving those applications a chance to prompt for unsaved work. Unsaved data may be permanently lost.\n\nEnable this behaviour?",
                    "Confirm force-close setting",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (enableForceClose != DialogResult.Yes)
                    _forceCloseApplications.Checked = false;
            }

            config.Settings.ForceCloseApplications = _forceCloseApplications.Checked;
            config.Settings.CountdownSeconds = Math.Clamp(seconds, 1, 86_400);
            _repository.SaveSettings(config.Settings);
            _logger.Info("Application settings saved.");
            MessageBox.Show(this, "Settings saved successfully.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshDashboard();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not save settings.", exception);
            MessageBox.Show(this, $"Settings could not be saved.\n\n{exception.Message}", "Settings error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshAllViews()
    {
        RefreshScheduleGrid();
        RefreshDashboardGrid();
        RefreshHistoryGrid();
        RefreshDashboard();
    }

    private void RefreshScheduleGrid()
    {
        var selectedId = _lastSelectedScheduleId ?? SelectedSchedule()?.Id;
        _scheduleGrid.Rows.Clear();
        foreach (var schedule in _repository.GetSchedulesSnapshot().OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var when = DescribeSchedule(schedule);
            var status = schedule.IsEnabled ? (schedule.SnoozedUntilLocal.HasValue ? "Snoozed" : "Enabled") : "Disabled";
            var rowIndex = _scheduleGrid.Rows.Add(schedule.Name, schedule.Type, schedule.Action, when, status);
            _scheduleGrid.Rows[rowIndex].Tag = schedule;
            if (selectedId == schedule.Id)
            {
                _scheduleGrid.Rows[rowIndex].Selected = true;
                _scheduleGrid.CurrentCell = _scheduleGrid.Rows[rowIndex].Cells[0];
            }
        }

        // Keep the selection identity even if the grid temporarily loses focus.
        _lastSelectedScheduleId = selectedId;
    }

    private void RefreshDashboardGrid()
    {
        _dashboardGrid.Rows.Clear();
        var now = DateTime.Now;
        var enabledSchedules = _repository.GetSchedulesSnapshot()
            .Where(s => s.IsEnabled)
            .Select(s => new { Schedule = s, Next = ScheduleCalculator.GetNextOccurrence(s, now) })
            .Where(x => x.Next.HasValue)
            .OrderBy(x => x.Next)
            .Take(12);

        foreach (var item in enabledSchedules)
        {
            var row = _dashboardGrid.Rows.Add(item.Schedule.Name, item.Schedule.Action, item.Next!.Value.ToString("ddd, dd MMM yyyy HH:mm:ss"), item.Schedule.SnoozedUntilLocal.HasValue ? "Snoozed" : "Enabled");
            _dashboardGrid.Rows[row].Tag = item.Schedule;
        }
    }

    private void RefreshHistoryGrid()
    {
        _historyGrid.Rows.Clear();
        foreach (var entry in _repository.GetSnapshot().History)
            _historyGrid.Rows.Add(entry.TimestampLocal.ToString("dd MMM yyyy HH:mm:ss"), entry.ScheduleName, entry.Action, entry.Result, entry.Details);
    }

    private void RefreshDashboard()
    {
        if (IsDisposed)
            return;

        var now = DateTime.Now;
        var enabled = _repository.GetSchedulesSnapshot().Where(s => s.IsEnabled).ToList();
        var next = enabled
            .Select(s => new { Schedule = s, Due = ScheduleCalculator.GetNextOccurrence(s, now) })
            .Where(x => x.Due.HasValue)
            .OrderBy(x => x.Due)
            .FirstOrDefault();

        if (next is null)
        {
            _nextOperationValue.Text = "No upcoming action";
            _nextTimeValue.Text = "—";
            _remainingValue.Text = "—";
        }
        else
        {
            _nextOperationValue.Text = next.Schedule.Action.ToString();
            _nextTimeValue.Text = next.Due!.Value.ToString("dd MMM, HH:mm");
            var remaining = next.Due.Value - now;
            _remainingValue.Text = FormatRemaining(remaining);
        }

        _scheduleStatusValue.Text = enabled.Count == 0 ? "Disabled" : $"{enabled.Count} active";
        _dashboardSummary.Text = $"{_repository.GetSchedulesSnapshot().Count} schedule(s) configured · {enabled.Count} enabled · checks run in the background";
        _quickToggleButton.Text = enabled.Count > 0 ? "Disable all schedules" : "Enable all schedules";
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        var days = (int)remaining.TotalDays;
        return days > 0
            ? $"{days}d {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static string DescribeSchedule(ScheduleItem schedule)
    {
        var time = schedule.TimeOfDay.ToString("HH:mm");
        return schedule.Type switch
        {
            ScheduleType.Daily => $"Every day at {time}",
            ScheduleType.Weekly => $"{string.Join(", ", schedule.Weekdays.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString()[..3]))} at {time}",
            ScheduleType.OneTime => schedule.OneTimeDateTimeLocal?.ToString("dd MMM yyyy HH:mm") ?? "Date not set",
            _ => "—"
        };
    }

    private ScheduleItem? SelectedSchedule()
    {
        // First resolve the last explicitly clicked row by its stable ID. Toolbar
        // clicks can move focus and clear DataGridView.SelectedRows/CurrentCell.
        if (_lastSelectedScheduleId is Guid rememberedId)
        {
            var remembered = _repository.GetSchedulesSnapshot()
                .FirstOrDefault(schedule => schedule.Id == rememberedId);
            if (remembered is not null)
                return remembered;

            _lastSelectedScheduleId = null;
        }

        DataGridViewRow? row = _scheduleGrid.SelectedRows.Count > 0
            ? _scheduleGrid.SelectedRows[0]
            : _scheduleGrid.CurrentRow;

        if (row is null && _scheduleGrid.CurrentCell is DataGridViewCell currentCell &&
            currentCell.RowIndex >= 0 && currentCell.RowIndex < _scheduleGrid.Rows.Count)
        {
            row = _scheduleGrid.Rows[currentCell.RowIndex];
        }

        if (row?.Tag is not ScheduleItem schedule)
            return null;

        _lastSelectedScheduleId = schedule.Id;
        return schedule.Clone();
    }

    private void AddSchedule()
    {
        using var editor = new ScheduleEditorForm();
        if (editor.ShowDialog(this) != DialogResult.OK || editor.ResultSchedule is null)
            return;
        try
        {
            _repository.AddSchedule(editor.ResultSchedule);
            _logger.Info($"Added schedule '{editor.ResultSchedule.Name}'.");
            RefreshAllViews();
        }
        catch (Exception exception)
        {
            ShowRepositoryError("Could not add schedule", exception);
        }
    }

    private void EditSelectedSchedule()
    {
        var selected = SelectedSchedule();
        if (selected is null)
        {
            MessageBox.Show(this, "Select a schedule to edit.", "No schedule selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // Resolve the latest persisted copy by ID rather than relying only on the
        // DataGridView row's cached Tag object.
        var scheduleToEdit = _repository.GetSchedulesSnapshot()
            .FirstOrDefault(schedule => schedule.Id == selected.Id);
        if (scheduleToEdit is null)
        {
            MessageBox.Show(this, "This schedule no longer exists. Refresh the schedule list and try again.",
                "Schedule not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshScheduleGrid();
            return;
        }

        _lastSelectedScheduleId = scheduleToEdit.Id;
        _logger.Info(
            $"Opening schedule editor for ID={scheduleToEdit.Id}, name='{scheduleToEdit.Name}', " +
            $"type={scheduleToEdit.Type}, action={scheduleToEdit.Action}, " +
            $"time={scheduleToEdit.TimeOfDay:HH:mm}, date={scheduleToEdit.OneTimeDateTimeLocal:O}, " +
            $"weekdays=[{string.Join(",", scheduleToEdit.Weekdays ?? new List<DayOfWeek>())}], " +
            $"enabled={scheduleToEdit.IsEnabled}.");

        using var editor = new ScheduleEditorForm(scheduleToEdit);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.ResultSchedule is null)
            return;
        try
        {
            _repository.UpdateSchedule(editor.ResultSchedule);
            _logger.Info($"Updated schedule '{editor.ResultSchedule.Name}'.");
            RefreshAllViews();
        }
        catch (Exception exception)
        {
            ShowRepositoryError("Could not update schedule", exception);
        }
    }

    private void ToggleSelectedSchedule()
    {
        var selected = SelectedSchedule();
        if (selected is null)
        {
            MessageBox.Show(this, "Select a schedule first.", "No schedule selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            _repository.SetScheduleEnabled(selected.Id, !selected.IsEnabled);
            RefreshAllViews();
        }
        catch (Exception exception)
        {
            ShowRepositoryError("Could not change schedule status", exception);
        }
    }

    private void DeleteSelectedSchedule()
    {
        var selected = SelectedSchedule();
        if (selected is null)
        {
            MessageBox.Show(this, "Select a schedule to delete.", "No schedule selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete the schedule '{selected.Name}'?", "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            _repository.DeleteSchedule(selected.Id);
            _logger.Info($"Deleted schedule '{selected.Name}'.");
            RefreshAllViews();
        }
        catch (Exception exception)
        {
            ShowRepositoryError("Could not delete schedule", exception);
        }
    }

    private void ToggleAllSchedules()
    {
        var enable = !_repository.AreAnySchedulesEnabled();
        try
        {
            _repository.SetAllSchedulesEnabled(enable);
            _logger.Info(enable ? "All schedules enabled." : "All schedules disabled.");
            RefreshAllViews();
        }
        catch (Exception exception)
        {
            ShowRepositoryError("Could not change schedule status", exception);
        }
    }

    private async Task ConfirmAndExecuteImmediateAsync(PowerAction action)
    {
        var settings = _repository.GetSnapshot().Settings;
        var forceClose = settings.ForceCloseApplications && (action is PowerAction.Shutdown or PowerAction.Restart);
        var forceWarning = forceClose
            ? "\n\nForce-close is enabled: running applications will be closed without warning. Unsaved data may be lost."
            : string.Empty;
        var confirmation = MessageBox.Show(
            this,
            $"Are you sure you want to {ImmediateVerb(action)} now?\n\nThis is an immediate Windows operation and may close applications or end your session.{forceWarning}",
            $"Confirm {action}",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
            return;

        try
        {
            var result = await _powerActions.ExecuteAsync(action, forceCloseApplications: forceClose);
            _repository.AddHistory("Immediate operation", action.ToString(), result.Success ? "Requested" : "Failed", result.Message);
            _logger.Info($"Immediate operation '{action}': {result.Message}");
            RefreshHistoryGrid();
            if (!result.Success)
                MessageBox.Show(this, result.Message, "Power operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception exception)
        {
            _logger.Error($"Immediate operation '{action}' failed.", exception);
            MessageBox.Show(this, exception.Message, "Power operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task HandleScheduledTriggerAsync(ScheduleTrigger trigger, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void BeginHandling()
        {
            _ = HandleScheduledTriggerOnUiAsync(trigger, cancellationToken, completion);
        }

        try
        {
            if (IsDisposed || !IsHandleCreated)
            {
                _repository.CompleteOccurrence(trigger.Schedule.Id);
                completion.TrySetResult();
            }
            else if (InvokeRequired)
            {
                BeginInvoke((Action)BeginHandling);
            }
            else
            {
                BeginHandling();
            }
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        await completion.Task;
    }

    private async Task HandleScheduledTriggerOnUiAsync(
        ScheduleTrigger trigger,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _repository.GetSnapshot().Settings;
            var forceClose = settings.ForceCloseApplications && (trigger.Schedule.Action is PowerAction.Shutdown or PowerAction.Restart);
            if (settings.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(
                    8_000,
                    "Scheduled operation starting",
                    $"{trigger.Schedule.Action} is scheduled for '{trigger.Schedule.Name}'. You can cancel or snooze it.",
                    ToolTipIcon.Info);
            }

            CountdownOutcome outcome;
            using (var countdown = new CountdownForm(trigger.Schedule, settings.CountdownSeconds, forceClose))
            {
                countdown.ShowDialog(this);
                outcome = countdown.Outcome;
            }
            if (outcome.Kind == CountdownOutcomeKind.Snoozed)
            {
                _repository.SnoozeSchedule(trigger.Schedule.Id, outcome.SnoozeMinutes);
                _repository.AddHistory(trigger.Schedule.Name, trigger.Schedule.Action.ToString(), "Snoozed", $"Deferred by {outcome.SnoozeMinutes} minutes.");
                _logger.Info($"Schedule '{trigger.Schedule.Name}' snoozed for {outcome.SnoozeMinutes} minutes.");
            }
            else if (outcome.Kind == CountdownOutcomeKind.Cancelled)
            {
                _repository.CompleteOccurrence(trigger.Schedule.Id);
                _repository.AddHistory(trigger.Schedule.Name, trigger.Schedule.Action.ToString(), "Cancelled", "The pending operation was cancelled before the Windows power command was issued.");
                _logger.Info($"Pending operation for schedule '{trigger.Schedule.Name}' was cancelled.");
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await _powerActions.ExecuteAsync(trigger.Schedule.Action, cancellationToken, forceClose);
                _repository.CompleteOccurrence(trigger.Schedule.Id);
                _repository.AddHistory(trigger.Schedule.Name, trigger.Schedule.Action.ToString(), result.Success ? "Requested" : "Failed", result.Message);
                _logger.Info($"Scheduled operation '{trigger.Schedule.Action}' for '{trigger.Schedule.Name}': {result.Message}");

                if (!result.Success)
                    MessageBox.Show(this, result.Message, "Scheduled power operation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshAllViews();
        }
        catch (OperationCanceledException)
        {
            try { _repository.CompleteOccurrence(trigger.Schedule.Id); }
            catch (Exception exception) { _logger.Error("Could not complete a cancelled occurrence.", exception); }
        }
        catch (Exception exception)
        {
            _logger.Error($"Scheduled operation for '{trigger.Schedule.Name}' failed.", exception);
            try
            {
                _repository.CompleteOccurrence(trigger.Schedule.Id);
                _repository.AddHistory(trigger.Schedule.Name, trigger.Schedule.Action.ToString(), "Error", exception.Message);
            }
            catch (Exception persistenceException)
            {
                _logger.Error("Could not record scheduled operation failure.", persistenceException);
            }
            if (!IsDisposed)
                MessageBox.Show(this, exception.Message, "Scheduled operation error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private static string ImmediateVerb(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "shut down the computer",
        PowerAction.Restart => "restart Windows",
        PowerAction.Sleep => "put the computer to sleep",
        PowerAction.Hibernate => "hibernate the computer",
        PowerAction.LogOff => "sign out of Windows",
        PowerAction.Lock => "lock the computer",
        _ => "perform this operation"
    };

    private void ShowRepositoryError(string title, Exception exception)
    {
        _logger.Error(title, exception);
        MessageBox.Show(this, $"{title}.\n\n{exception.Message}", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void ShowFromTray()
    {
        if (IsDisposed)
            return;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    protected override void SetVisibleCore(bool value)
    {
        // Application.Run normally shows its main form automatically. Suppress that first
        // show when starting minimized, instead of showing the window and hiding it in OnShown.
        if (value && _hideOnInitialShow && !_initialShowRequestHandled)
        {
            _initialShowRequestHandled = true;
            // Ensure the hidden main-form handle exists for Application.Run's message loop.
            if (!IsHandleCreated)
                CreateHandle();
            base.SetVisibleCore(false);
            return;
        }

        if (value)
            _initialShowRequestHandled = true;

        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit && !_exiting)
        {
            var settings = _repository.GetSnapshot().Settings;
            if (settings.MinimizeToTrayOnClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (settings.ShowNotifications)
                    _notifyIcon.ShowBalloonTip(2500, "Still running", "Shutdown Manager continues monitoring schedules in the system tray.", ToolTipIcon.Info);
                return;
            }

            e.Cancel = true;
            _ = ExitApplicationAsync();
            return;
        }
        base.OnFormClosing(e);
    }

    private async Task ExitApplicationAsync()
    {
        if (_exiting)
            return;
        _exiting = true;
        _uiTimer.Stop();
        try
        {
            if (_scheduler is not null)
                await _scheduler.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Error while stopping the scheduler.", exception);
        }
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _allowExit = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiTimer.Dispose();
            if (_scheduler is not null)
                _ = _scheduler.DisposeAsync();
            _notifyIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
