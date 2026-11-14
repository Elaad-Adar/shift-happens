namespace ShiftHappens;

/// <summary>Global hotkeys through RegisterHotKey on a hidden message window.</summary>
sealed class HotKeys : NativeWindow, IDisposable
{
    readonly Dictionary<int, Action> _actions = new();

    public HotKeys() => CreateHandle(new CreateParams());

    /// <summary>False when another app already owns the combination.</summary>
    public bool Register(int id, Shortcut s, Action action)
    {
        Unregister(id);
        if (!Native.RegisterHotKey(Handle, id, s.Modifiers | Native.MOD_NOREPEAT, s.Key)) return false;
        _actions[id] = action;
        return true;
    }

    public void Unregister(int id)
    {
        if (_actions.Remove(id)) Native.UnregisterHotKey(Handle, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToList()) Unregister(id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
        {
            action();
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
