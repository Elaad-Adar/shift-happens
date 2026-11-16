using ShiftCore;

namespace ShiftHappens;

/// <summary>Settings: the two hotkeys, keyboard switching, start with Windows.</summary>
sealed class SettingsForm : Form
{
    readonly TrayApp _app;
    readonly HotkeyBox _convertBox = new();
    readonly HotkeyBox _forceBox = new();
    readonly CheckBox _wordBox = new() { Text = "With nothing selected, fix the word before the cursor", AutoSize = true };
    readonly CheckBox _switchBox = new() { Text = "Switch keyboard to the new language after a swap", AutoSize = true };
    readonly CheckBox _startupBox = new() { Text = "Start with Windows", AutoSize = true };
    readonly CheckBox _updatesBox = new() { Text = "Check for updates once a day", AutoSize = true };
    readonly Label _updateStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    readonly Button _updateButton = new() { Text = "Check now", AutoSize = true };
    readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) };
    readonly Label _hint = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) };
    readonly ComboBox _doubleTapBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };

    public SettingsForm(TrayApp app)
    {
        _app = app;
        Text = "Shift Happens Settings";
        Icon = TrayApp.AppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = SystemFonts.MessageBoxFont ?? Font;

        var s = app.Settings;
        _convertBox.Shortcut = s.ConvertShortcut;
        _forceBox.Shortcut = s.ForceSwapShortcut;
        foreach (var box in new[] { _convertBox, _forceBox })
        {
            box.Enter += (_, _) =>
            {
                _app.PauseHotKeys();
                _hint.Text = "Press the new shortcut: Ctrl, Alt or Win with a key, or an F key alone.";
            };
            box.Leave += (_, _) => Save();
            box.Rejected += key => _hint.Text =
                $"{key} alone can't be a shortcut, it would fire while you type. Add Ctrl, Alt or Win, or use an F key.";
            box.Recorded += RefreshHint;
        }

        _doubleTapBox.Items.Add("Off");
        foreach (var key in Enum.GetValues<DoubleTapKey>()) _doubleTapBox.Items.Add(key.ToString());
        _doubleTapBox.SelectedIndex = s.DoubleTapKey is { } dt ? (int)dt + 1 : 0;
        _doubleTapBox.SelectedIndexChanged += (_, _) =>
        {
            s.DoubleTapKey = _doubleTapBox.SelectedIndex > 0 ? (DoubleTapKey)(_doubleTapBox.SelectedIndex - 1) : null;
            s.Save();
            _app.RegisterHotKeys();
            _app.ApplyDoubleTap();
            RefreshStatus();
        };
        // Leave does not fire when the whole window loses focus, so re-register here too,
        // and pause again if the user comes back into a hotkey box.
        Deactivate += (_, _) => Save();
        Activated += (_, _) => { if (ActiveControl is HotkeyBox) _app.PauseHotKeys(); };

        var clearForce = new Button { Text = "Clear", AutoSize = true };
        clearForce.Click += (_, _) => { _forceBox.Shortcut = null; Save(); };
        var clearConvert = new Button { Text = "Clear", AutoSize = true };
        clearConvert.Click += (_, _) => { _convertBox.Shortcut = null; Save(); };

        _wordBox.Checked = s.FixWordBeforeCursor;
        _wordBox.CheckedChanged += (_, _) => { s.FixWordBeforeCursor = _wordBox.Checked; s.Save(); };

        _switchBox.Checked = s.SwitchLayoutAfterSwap;
        _switchBox.CheckedChanged += (_, _) => { s.SwitchLayoutAfterSwap = _switchBox.Checked; s.Save(); };

        _startupBox.Checked = Startup.Enabled;
        _startupBox.CheckedChanged += (_, _) =>
        {
            try { Startup.Enabled = _startupBox.Checked; }
            catch (Exception e) { _status.Text = "Start with Windows failed: " + e.Message; }
        };

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Click a box and press the new keys (Ctrl, Alt or Win with a key, or an F key alone).\n\n" +
                   "Fix: typed on the wrong keyboard → swaps it; right keyboard → fixes spelling.\n" +
                   "    akuo → שלום,  teh → the\n" +
                   "Swap: always swaps key for key, no spell check. For when Fix guesses wrong.\n" +
                   "    teh → אקי\n" +
                   "Double-tap: off by default. When on, Shift Happens uses a keyboard hook to notice the " +
                   "double tap; it looks only at the chosen key and keeps nothing.",
        };

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top };
        grid.Controls.Add(new Label { Text = "Fix selection:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 0);
        grid.Controls.Add(_convertBox, 1, 0);
        grid.Controls.Add(clearConvert, 2, 0);
        grid.Controls.Add(new Label { Text = "Swap selection:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 1);
        grid.Controls.Add(_forceBox, 1, 1);
        grid.Controls.Add(clearForce, 2, 1);
        grid.Controls.Add(new Label { Text = "Double-tap for Fix:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 2);
        grid.Controls.Add(_doubleTapBox, 1, 2);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        _updatesBox.Checked = s.CheckForUpdates;
        _updatesBox.CheckedChanged += (_, _) =>
        {
            s.CheckForUpdates = _updatesBox.Checked;
            s.Save();
            if (_updatesBox.Checked) _app.Updates.CheckIfDue();
        };

        var versionLabel = new Label
        {
            Text = $"Shift Happens {AppInfo.Version}",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
        };
        var notes = new LinkLabel { Text = "Release notes", AutoSize = true, Anchor = AnchorStyles.Left };
        notes.LinkClicked += (_, _) => AppInfo.Open(Releases.NotesUrl(AppInfo.Version));
        _updateButton.Click += (_, _) =>
        {
            if (_app.Updates.Available is { } update) AppInfo.Open(update.Url);
            else _ = _app.Updates.CheckAsync();
        };
        var versionRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        versionRow.Controls.AddRange(new Control[] { versionLabel, notes, _updateButton });

        var report = new Button { Text = "Report a problem…", AutoSize = true };
        report.Click += (_, _) => AppInfo.OpenIssue(Releases.IssueKind.Bug);
        var idea = new Button { Text = "Suggest an idea…", AutoSize = true };
        idea.Click += (_, _) => AppInfo.OpenIssue(Releases.IssueKind.Idea);
        var feedbackRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        feedbackRow.Controls.AddRange(new Control[] { report, idea });

        _app.Updates.Changed += RefreshUpdate;
        RefreshUpdate();

        stack.Controls.AddRange(new Control[]
        {
            versionRow, _updateStatus, grid, _hint, help, _wordBox, _switchBox, _startupBox, _updatesBox, feedbackRow, _status,
        });
        Controls.Add(stack);
        RefreshStatus();
    }

    void Save()
    {
        var s = _app.Settings;
        s.ConvertShortcut = _convertBox.Shortcut;
        s.ForceSwapShortcut = _forceBox.Shortcut;
        s.Save();
        _app.RegisterHotKeys();
        RefreshStatus();
    }

    void RefreshUpdate()
    {
        if (IsDisposed) return;
        if (_app.Updates.Available is { } update)
        {
            _updateButton.Text = $"Download {update.Version}…";
            _updateStatus.Text = $"Version {update.Version} is available.";
        }
        else
        {
            _updateButton.Text = "Check now";
            _updateStatus.Text = _app.Updates.Status;
        }
    }

    void RefreshStatus()
    {
        _status.Text = string.Join(Environment.NewLine, _app.HotkeyProblems);
        RefreshHint();
    }

    /// <summary>A note under the shortcuts when an F key alone is used, else nothing.</summary>
    void RefreshHint()
    {
        var bare = new[] { _convertBox.Shortcut, _forceBox.Shortcut }.OfType<Shortcut>().Where(x => x.IsBareFunctionKey).ToList();
        _hint.Text = bare.Count == 0 ? "" :
            string.Join(", ", bare.Select(x => x.Display)) + " alone works in every app. F10 alone normally opens app " +
            "menus, and some programs (like Visual Studio) use F keys.";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _app.Updates.Changed -= RefreshUpdate;
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Save();
        base.OnFormClosing(e);
    }
}

/// <summary>Read-only box that shows a shortcut and records the next key combination pressed in it.</summary>
sealed class HotkeyBox : TextBox
{
    Shortcut? _shortcut;

    /// <summary>A key without Ctrl, Alt or Win (and not an F key) was pressed; gives its name.</summary>
    public event Action<string>? Rejected;
    public event Action? Recorded;

    public HotkeyBox()
    {
        ReadOnly = true;
        Width = 180;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        UpdateText();
    }

    public Shortcut? Shortcut
    {
        get => _shortcut;
        set { _shortcut = value; UpdateText(); }
    }

    void UpdateText() => Text = _shortcut?.Display ?? "Not set";

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin or Keys.None)
            return true;
        if (key == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((keyData & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
        if ((Native.GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0)
            mods |= Native.MOD_WIN;

        var candidate = new Shortcut(mods, (uint)key);
        if (!HotkeyRule.Allowed((mods & (Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN)) != 0, candidate.IsFunctionKey))
        {
            System.Media.SystemSounds.Beep.Play();
            Rejected?.Invoke(candidate.Display);
            return true;
        }
        Shortcut = candidate;
        Recorded?.Invoke();
        return true;
    }
}
