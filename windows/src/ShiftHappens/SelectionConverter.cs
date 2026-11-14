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
            if (text is null && _settings.FixWordBeforeCursor) text = await SelectWordBeforeCursor();
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
        return s.Length > 0 ? s : null;
    }

    /// <summary>Nothing selected: select the word just before the cursor with Shift+Left steps (see
    /// WordWalk) and return it, or null (cursor put back) when there is none.</summary>
    static async Task<string?> SelectWordBeforeCursor()
    {
        var walk = new WordWalk();
        var last = "";
        for (int i = 0; i < walk.MaxCharacters * 2 + 4; i++)
        {
            Native.Arrow(Native.VK_LEFT, shift: true);
            var selection = await CopySelection(250) ?? last;
            switch (walk.Next(selection))
            {
                case WordWalk.Step.Extend:
                    last = selection;
                    continue;
                case WordWalk.Step.Done:
                    return selection;
                case WordWalk.Step.DoneShrink:
                    Native.Arrow(Native.VK_RIGHT, shift: true);
                    return await CopySelection(250);
            }
            break;  // Fail
        }
        Native.Arrow(Native.VK_RIGHT, shift: false);  // collapse the selection back to the cursor
        return null;
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
