using Microsoft.Win32;
using ShiftCore;

namespace ShiftHappens;

/// <summary>The tray icon, its menu, and the hotkeys.</summary>
sealed class TrayApp : ApplicationContext
{
    const int ConvertId = 1;
    const int ForceId = 2;

    readonly Settings _settings = Settings.Load();
    readonly SelectionConverter _selection;
    readonly HotKeys _hotKeys = new();
    readonly DoubleTapMonitor _doubleTap = new();
    readonly NotifyIcon _tray;
    SettingsForm? _settingsForm;

    public List<string> HotkeyProblems { get; } = new();
    public Settings Settings => _settings;
    public UpdateChecker Updates { get; }

    public static Icon AppIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    public TrayApp()
    {
        _selection = new SelectionConverter(_settings);
        Updates = new UpdateChecker(_settings);
        _tray = new NotifyIcon
        {
            Icon = AppIcon(),
            Text = "Shift Happens",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu(_tray.ContextMenuStrip);
        _tray.MouseDoubleClick += (_, _) => OpenSettings();

        RegisterHotKeys();
        ApplyDoubleTap();
        Updates.Start();

        // Keyboards added or removed in Windows settings.
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

        if (HotkeyProblems.Count > 0)
        {
            // Another app owns the shortcut: say so and open Settings to pick another one.
            _tray.ShowBalloonTip(8000, "Shift Happens: pick another hotkey",
                string.Join(Environment.NewLine, HotkeyProblems), ToolTipIcon.Warning);
            OpenSettings();
        }
        else if (!_settings.WelcomeShown)
        {
            var key = _settings.ConvertShortcut?.Display ?? "the hotkey";
            _tray.ShowBalloonTip(5000, "Shift Happens is running",
                $"Select text typed on the wrong keyboard and press {key}.", ToolTipIcon.Info);
            _settings.WelcomeShown = true;
            _settings.Save();
        }
    }

    void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Locale) _selection.ReloadLayouts();
    }

    public void RegisterHotKeys()
    {
        _hotKeys.UnregisterAll();
        HotkeyProblems.Clear();
        if (_settings.ConvertShortcut is { } c && !_hotKeys.Register(ConvertId, c, () => _selection.Run(Mode.Smart)))
            HotkeyProblems.Add($"{c.Display} is taken by another app");
        if (_settings.ForceSwapShortcut is { } f && !_hotKeys.Register(ForceId, f, () => _selection.Run(Mode.ForceSwap)))
            HotkeyProblems.Add($"{f.Display} is taken by another app");
    }

    public void PauseHotKeys() => _hotKeys.UnregisterAll();

    /// <summary>Starts or stops the double-tap keyboard hook, per Settings (off by default).</summary>
    public void ApplyDoubleTap()
    {
        _doubleTap.Stop();
        if (_settings.DoubleTapKey is { } key && !_doubleTap.Start(key, () => _selection.Run(Mode.Smart)))
            HotkeyProblems.Add($"Double-tap {key} could not be turned on");
    }

    /// <summary>"Ctrl+Shift+Space or Ctrl Ctrl" for the menu: either one runs Fix.</summary>
    string? FixKeys()
    {
        var keys = new List<string>();
        if (_settings.ConvertShortcut is { } c) keys.Add(c.Display);
        if (_settings.DoubleTapKey is { } d) keys.Add($"{d} {d}");
        return keys.Count == 0 ? null : string.Join(" or ", keys);
    }

    void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        if (Updates.Available is { } update)
        {
            menu.Items.Add($"Update to {update.Version}…", null, (_, _) => AppInfo.Open(update.Url));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add(new ToolStripMenuItem("Fix selection", null, (_, _) => RunFromMenu(Mode.Smart))
        {
            ShortcutKeyDisplayString = FixKeys(),
        });
        menu.Items.Add(new ToolStripMenuItem("Swap selection", null, (_, _) => RunFromMenu(Mode.ForceSwap))
        {
            ShortcutKeyDisplayString = _settings.ForceSwapShortcut?.Display,
        });

        var notes = new List<string> { _selection.LastStatus };
        notes.AddRange(HotkeyProblems);
        if (!_selection.HasHebrewSpelling) notes.Add("No Hebrew spell checker: Hebrew typos are not fixed");
        foreach (var note in notes.Where(n => n.Length > 0))
            menu.Items.Add(new ToolStripMenuItem(note) { Enabled = false });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Check for updates…", null, (_, _) => { OpenSettings(); _ = Updates.CheckAsync(); });
        menu.Items.Add("Report a problem…", null, (_, _) => AppInfo.OpenIssue(Releases.IssueKind.Bug));
        menu.Items.Add("Suggest an idea…", null, (_, _) => AppInfo.OpenIssue(Releases.IssueKind.Idea));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem($"Shift Happens {AppInfo.Version}") { Enabled = false });
        menu.Items.Add("Quit", null, (_, _) => Quit());
    }

    // The menu takes focus from the user's app; give it back before Ctrl+C goes out.
    async void RunFromMenu(Mode mode)
    {
        await Task.Delay(200);
        _selection.Run(mode);
    }

    void OpenSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed) _settingsForm = new SettingsForm(this);
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    void Quit()
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _hotKeys.Dispose();
        _doubleTap.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }
}
