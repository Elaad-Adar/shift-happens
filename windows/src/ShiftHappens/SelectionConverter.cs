using System.Media;
using System.Runtime.InteropServices;
using ShiftCore;

namespace ShiftHappens;

/// <summary>Copy the selection, convert it, paste the result, restore the clipboard, switch the keyboard.
/// Runs on the UI (STA) thread, which the clipboard and the spell checker need.</summary>
sealed class SelectionConverter
{
    readonly Settings _settings;
    readonly WinSpellChecker _spell = new();
    Converter? _converter;
    LayoutPair? _pair;
    bool _busy;

    public string LastStatus { get; private set; } = "";

    /// <summary>Called when the hotkey runs but a keyboard is missing, with the reason. The tray shows it once.</summary>
    public Action<string>? MissingKeyboard { get; set; }
    public bool HasHebrewSpelling => _spell.HasHebrew;

    public SelectionConverter(Settings settings)
    {
        _settings = settings;
        ReloadLayouts();
    }

    public void ReloadLayouts()
    {
        try
        {
            _pair = WinLayouts.FindPair();
            _converter = new Converter(WinLayouts.Table(_pair), _spell);
            LastStatus = "";
        }
        catch (InvalidOperationException e)
        {
            _pair = null;
            _converter = null;
            LastStatus = e.Message;
        }
    }

    public async void Run(Mode mode)
    {
        if (_busy) return;
        // A keyboard added since the last check: read the layouts again before giving up.
        if (_converter is null) ReloadLayouts();
        if (_converter is null)
        {
            SystemSounds.Beep.Play();
            MissingKeyboard?.Invoke(LastStatus);
            return;
        }
        _busy = true;
        try
        {
            // The hotkey's own Ctrl/Alt are still down; Ctrl+C sent now would arrive as Ctrl+Alt+C.
            if (!await WaitFor(() => !Native.AnyModifierDown(), 1500))
            {
                LastStatus = "Release the hotkey keys to convert";
                SystemSounds.Beep.Play();
                return;
            }

            var saved = ClipboardSnapshot.Take();
            var text = await CopySelection(500);
            AutoSelection? autoSelection = null;
            if (text is null && _settings.FixWordBeforeCursor)
            {
                autoSelection = await SelectWordBeforeCursor();
                text = autoSelection?.Text;
            }
            if (text is null)
            {
                saved.Restore(_settings.KeepOutOfClipboardHistory);
                LastStatus = _settings.FixWordBeforeCursor ? "No word before the cursor" : "Nothing selected";
                SystemSounds.Beep.Play();
                return;
            }

            var result = _converter.Convert(text, mode);
            if (!result.Changed)
            {
                // Put the cursor back where it was instead of leaving our selection behind.
                autoSelection?.Restore();
                saved.Restore(_settings.KeepOutOfClipboardHistory);
                LastStatus = "Looks right already";
                return;
            }

            var converted = new DataObject();
            converted.SetData(DataFormats.UnicodeText, false, result.Text);
            if (!ClipboardSnapshot.Write(converted, _settings.KeepOutOfClipboardHistory))
            {
                // Pasting now would paste whatever is on the clipboard instead of the converted text.
                autoSelection?.Restore();
                saved.Restore(_settings.KeepOutOfClipboardHistory);
                LastStatus = "Clipboard busy, nothing changed";
                SystemSounds.Beep.Play();
                return;
            }
            uint ours = Native.GetClipboardSequenceNumber();
            Native.CtrlChord((ushort)Keys.V);

            if (result.SwitchTo is Script target && _settings.SwitchLayoutAfterSwap && _pair is not null)
                WinLayouts.Activate(target == Script.Hebrew ? _pair.Hebrew : _pair.Latin);
            LastStatus = Describe(result);

            // Let the app read the clipboard, then put the user's clipboard back unless something
            // else has written to it in the meantime.
            await Task.Delay(400);
            if (Native.GetClipboardSequenceNumber() == ours) saved.Restore(_settings.KeepOutOfClipboardHistory);
        }
        catch (Exception e)
        {
            ErrorLog.Write("convert", e);
            LastStatus = "Failed: " + e.Message;
            SystemSounds.Beep.Play();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Ctrl+C, then the copied text, or null when the clipboard did not change in time
    /// (nothing selected) or holds no text.</summary>
    static async Task<string?> CopySelection(int timeoutMs)
    {
        uint before = Native.GetClipboardSequenceNumber();
        Native.CtrlChord((ushort)Keys.C);
        if (!await WaitFor(() => Native.GetClipboardSequenceNumber() != before, timeoutMs)) return null;
        var s = Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : "");
        if (s.Length == 0 || IsEditorLineCopy()) return null;
        return s;
    }

    /// <summary>A selection this app made, and how to undo it (caret back where it was).</summary>
    sealed record AutoSelection(string Text, Action Restore);

    static string Lines(string s) => s.Replace("\r\n", "\n");

    /// <summary>An editor's whole-line copy of an empty selection (VS Code marks it in Chromium's custom
    /// clipboard data): counts as "nothing selected".</summary>
    static bool IsEditorLineCopy()
    {
        try
        {
            var data = Retry(() => Clipboard.GetData(LineCopy.WindowsClipboardFormat));
            var bytes = data switch
            {
                MemoryStream ms => ms.ToArray(),
                byte[] b => b,
                _ => null,
            };
            return bytes is not null && LineCopy.IsFromEmptySelection(bytes);
        }
        catch (Exception e)
        {
            ErrorLog.Write("line copy check", e);
            return false;
        }
    }

    /// <summary>Nothing selected: select the word just before the cursor. First through UI Automation
    /// (logical order, right for Hebrew); if the control has no text pattern, or the copy does not match,
    /// with Shift+Left steps. Null when there is no word; the caret is then where it was.</summary>
    static async Task<AutoSelection?> SelectWordBeforeCursor()
    {
        var outcome = UiaWord.Select();
        if (outcome.Kind == UiaWord.Kind.NoWord) return null;
        if (outcome.Selection is { } uia)
        {
            if (await CopySelection(500) is { } copied && Lines(copied) == Lines(uia.Expected))
                return new AutoSelection(copied, uia.Restore);
            uia.Restore();  // the control did not really select it: try the key walk
        }
        return await WalkWithKeys() is { } text
            ? new AutoSelection(text, () => Native.Arrow(Native.VK_RIGHT, shift: false))
            : null;
    }

    /// <summary>Fallback for controls without UI Automation text: extend with Shift+Left one character at
    /// a time (see WordWalk). Visual movement, so only reliable in left-to-right text. On null the caret is
    /// where it was: a selection this made is collapsed, and nothing is pressed when none was made.</summary>
    static async Task<string?> WalkWithKeys()
    {
        var walk = new WordWalk();
        var last = "";
        for (int i = 0; i < walk.MaxCharacters * 2 + 4; i++)
        {
            Native.Arrow(Native.VK_LEFT, shift: true);
            var selection = await CopyStep(hadSelection: last.Length > 0);
            if (selection is null) break;
            var step = walk.Next(selection);
            if (step == WordWalk.Step.Extend)
            {
                last = selection;
                continue;
            }
            if (step == WordWalk.Step.Done) return selection;
            if (step == WordWalk.Step.DoneShrink)
            {
                Native.Arrow(Native.VK_RIGHT, shift: true);
                if (await CopySelection(500) is { } final) return final;
            }
            if (selection.Length > 0) last = selection;
            break;  // Fail, or the final copy failed
        }
        if (last.Length > 0) Native.Arrow(Native.VK_RIGHT, shift: false);
        return null;
    }

    /// <summary>One copy during the walk, retried once for slow apps. With nothing selected yet, still no
    /// clipboard change means an empty selection (start of the field): "". With a selection already made,
    /// it is null (abort) rather than a guess that the field boundary was reached.</summary>
    static async Task<string?> CopyStep(bool hadSelection)
    {
        if (await CopySelection(250) is { } s) return s;
        if (await CopySelection(750) is { } t) return t;
        return hadSelection ? null : "";
    }

    static string Describe(Conversion r)
    {
        var parts = new List<string>();
        if (r.SwappedWords > 0) parts.Add($"swapped {r.SwappedWords} word{(r.SwappedWords == 1 ? "" : "s")}");
        if (r.FixedWords > 0) parts.Add($"fixed {r.FixedWords} word{(r.FixedWords == 1 ? "" : "s")}");
        var s = string.Join(", ", parts);
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs)
    {
        var end = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < end)
        {
            if (condition()) return true;
            await Task.Delay(10);
        }
        return condition();
    }

    /// <summary>The clipboard is briefly locked by whoever wrote to it last; retry a few times.</summary>
    internal static T Retry<T>(Func<T> action)
    {
        for (int i = 0; ; i++)
        {
            try { return action(); }
            catch (ExternalException) when (i < 10) { Thread.Sleep(30); }
        }
    }
}

/// <summary>The clipboard formats that can be read back and safely put back: text and raw data. Writing it
/// back hands Windows every format at once (copy: true), so nothing calls back into this app later.</summary>
sealed class ClipboardSnapshot
{
    /// <summary>Text formats WinForms writes as plain text. Any other format is kept only as raw bytes: an
    /// object such as a bitmap or a .NET value would need serializing when Windows asks for it, and a
    /// failure there happens inside a Windows callback and ends the process (#27).</summary>
    static readonly HashSet<string> TextFormats = new(StringComparer.Ordinal)
    {
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText, DataFormats.Rtf, DataFormats.Html,
        DataFormats.CommaSeparatedValue,
    };

    /// <summary>Windows' clipboard history and cloud clipboard formats (#28): content that carries them is
    /// left out of Win+V history and not synced.</summary>
    const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";
    const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
    const string CanUploadToCloud = "CanUploadToCloudClipboard";

    readonly List<(string Format, object Value)> _items;

    ClipboardSnapshot(List<(string, object)> items) => _items = items;

    public static ClipboardSnapshot Take()
    {
        var items = new List<(string, object)>();
        try
        {
            var src = SelectionConverter.Retry(Clipboard.GetDataObject);
            if (src is null) return new ClipboardSnapshot(items);
            foreach (var format in src.GetFormats(false))
            {
                try
                {
                    if (Keep(format, src.GetData(format, false)) is { } value) items.Add((format, value));
                }
                catch (Exception)
                {
                    // Some formats (delay-rendered, private) cannot be read back; skip them.
                }
            }
        }
        catch (ExternalException e)
        {
            ErrorLog.Write("clipboard read", e);
        }
        return new ClipboardSnapshot(items);
    }

    /// <summary>The value to keep for one format, as a type WinForms writes without serializing, or null.</summary>
    static object? Keep(string format, object? value) => value switch
    {
        string s when TextFormats.Contains(format) => s,
        string[] files when format == DataFormats.FileDrop => files,
        MemoryStream ms => new MemoryStream(ms.ToArray(), writable: false),
        byte[] bytes => new MemoryStream(bytes, writable: false),
        _ => null,
    };

    public void Restore(bool keepOutOfHistory)
    {
        var data = new DataObject();
        foreach (var (format, value) in _items)
        {
            if (value is MemoryStream ms) ms.Position = 0;
            data.SetData(format, false, value);
        }
        if (data.GetFormats().Length == 0)
        {
            try { SelectionConverter.Retry(() => { Clipboard.Clear(); return true; }); }
            catch (ExternalException e) { ErrorLog.Write("clipboard clear", e); }
            return;
        }
        if (Write(data, keepOutOfHistory)) return;
        // The full restore failed: fall back to the plain text, so a half-written clipboard is not left behind.
        if (_items.FirstOrDefault(i => i.Format == DataFormats.UnicodeText).Value is string text)
        {
            var plain = new DataObject();
            plain.SetData(DataFormats.UnicodeText, false, text);
            Write(plain, keepOutOfHistory);
        }
    }

    /// <summary>Puts <paramref name="data"/> on the clipboard, all formats rendered now. With
    /// <paramref name="keepOutOfHistory"/>, adds the formats that keep it out of Win+V history. False on failure.</summary>
    public static bool Write(DataObject data, bool keepOutOfHistory)
    {
        if (keepOutOfHistory)
        {
            // Each flag is a raw 4-byte DWORD. A plain int would be wrapped by .NET and Windows would ignore it.
            data.SetData(ExcludeFromMonitors, false, Dword(0));
            data.SetData(CanIncludeInHistory, false, Dword(0));
            data.SetData(CanUploadToCloud, false, Dword(0));
        }
        try
        {
            Clipboard.SetDataObject(data, copy: true, retryTimes: 10, retryDelay: 30);
            return true;
        }
        catch (Exception e) when (e is ExternalException or ThreadStateException or NotSupportedException)
        {
            ErrorLog.Write("clipboard write", e);
            return false;
        }
    }

    static MemoryStream Dword(uint value) => new(BitConverter.GetBytes(value), writable: false);
}
