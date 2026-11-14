namespace ShiftHappens;

/// <summary>Settings: the two hotkeys, keyboard switching, start with Windows.</summary>
sealed class SettingsForm : Form
{
    readonly TrayApp _app;
    readonly HotkeyBox _convertBox = new();
    readonly HotkeyBox _forceBox = new();
    readonly CheckBox _switchBox = new() { Text = "Switch keyboard to the new language after a swap", AutoSize = true };
    readonly CheckBox _startupBox = new() { Text = "Start with Windows", AutoSize = true };
    readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0) };

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
            box.Enter += (_, _) => _app.PauseHotKeys();
            box.Leave += (_, _) => Save();
        }

        var clearForce = new Button { Text = "Clear", AutoSize = true };
        clearForce.Click += (_, _) => { _forceBox.Shortcut = null; Save(); };
        var clearConvert = new Button { Text = "Clear", AutoSize = true };
        clearConvert.Click += (_, _) => { _convertBox.Shortcut = null; Save(); };

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
            Text = "Click a box and press the new keys (needs Ctrl, Alt or Win). " +
                   "Fix: wrong keyboard → swaps the text; right keyboard → fixes spelling. Swap: always swaps.",
        };

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top };
        grid.Controls.Add(new Label { Text = "Fix selection:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 0);
        grid.Controls.Add(_convertBox, 1, 0);
        grid.Controls.Add(clearConvert, 2, 0);
        grid.Controls.Add(new Label { Text = "Swap selection:", AutoSize = true, Anchor = AnchorStyles.Right }, 0, 1);
        grid.Controls.Add(_forceBox, 1, 1);
        grid.Controls.Add(clearForce, 2, 1);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Fill,
        };
        stack.Controls.AddRange(new Control[] { grid, help, _switchBox, _startupBox, _status });
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

    void RefreshStatus() => _status.Text = string.Join(Environment.NewLine, _app.HotkeyProblems);

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

        if ((mods & (Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_WIN)) == 0)
        {
            System.Media.SystemSounds.Beep.Play();  // a hotkey needs Ctrl, Alt or Win
            return true;
        }
        Shortcut = new Shortcut(mods, (uint)key);
        return true;
    }
}
