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
        if (_converter is null)
        {
            SystemSounds.Beep.Play();
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
                saved.Restore();
                LastStatus = _settings.FixWordBeforeCursor ? "No word before the cursor" : "Nothing selected";
                SystemSounds.Beep.Play();
                return;
            }

            var result = _converter.Convert(text, mode);
            if (!result.Changed)
            {
                // Put the cursor back where it was instead of leaving our selection behind.
                autoSelection?.Restore();
                saved.Restore();
                LastStatus = "Looks right already";
                return;
            }

            Retry(() => { Clipboard.SetText(result.Text); return true; });
            uint ours = Native.GetClipboardSequenceNumber();
            Native.CtrlChord((ushort)Keys.V);

            if (result.SwitchTo is Script target && _settings.SwitchLayoutAfterSwap && _pair is not null)
                WinLayouts.Activate(target == Script.Hebrew ? _pair.Hebrew : _pair.Latin);
            LastStatus = Describe(result);

            // Let the app read the clipboard, then put the user's clipboard back unless something
            // else has written to it in the meantime.
            await Task.Delay(400);
            if (Native.GetClipboardSequenceNumber() == ours) saved.Restore();
        }
        catch (Exception e)
        {
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
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Nothing selected: select the word just before the cursor. First through UI Automation
    /// (logical order, right for Hebrew); if the control has no text pattern, or the copy does not match,
    /// with Shift+Left steps. Null when there is no word; the caret is then where it was.</summary>
    static async Task<AutoSelection?> SelectWordBeforeCursor()
    {
        if (UiaWord.Select() is { } uia)
        {
            if (await CopySelection(500) is { } copied && Lines(copied) == Lines(uia.Expected))
                return new AutoSelection(copied, uia.Restore);
            uia.Restore();
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

/// <summary>Every format on the clipboard that can be read back, so it can be put back.</summary>
sealed class ClipboardSnapshot
{
    readonly DataObject? _data;

    ClipboardSnapshot(DataObject? data) => _data = data;

    public static ClipboardSnapshot Take()
    {
        try
        {
            var src = SelectionConverter.Retry(Clipboard.GetDataObject);
            if (src is null) return new ClipboardSnapshot(null);
            var copy = new DataObject();
            foreach (var format in src.GetFormats(false))
            {
                try
                {
                    if (src.GetData(format, false) is { } value) copy.SetData(format, false, value);
                }
                catch (Exception)
                {
                    // Some formats (delay-rendered, private) cannot be read back; skip them.
                }
            }
            return new ClipboardSnapshot(copy);
        }
        catch (ExternalException)
        {
            return new ClipboardSnapshot(null);
        }
    }

    public void Restore()
    {
        try
        {
            if (_data is null || _data.GetFormats().Length == 0) SelectionConverter.Retry(() => { Clipboard.Clear(); return true; });
            else Clipboard.SetDataObject(_data, copy: true, retryTimes: 10, retryDelay: 30);
        }
        catch (ExternalException)
        {
            // Leave the converted text on the clipboard rather than crash.
        }
    }
}
