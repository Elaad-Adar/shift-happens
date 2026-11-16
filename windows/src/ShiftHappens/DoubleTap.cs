using System.Diagnostics;
using System.Runtime.InteropServices;
using ShiftCore;

namespace ShiftHappens;

/// <summary>Modifier keys that can be double-tapped to run Fix.</summary>
enum DoubleTapKey { Ctrl, Alt, Shift }

/// <summary>Runs an action when one modifier key is double-tapped (e.g. Ctrl, Ctrl). Windows has no hotkey
/// for that, so this installs a low-level keyboard hook while the option is on (off by default). The hook
/// sees every key press; it only asks "is this the chosen modifier, and is it going down or up", keeps no
/// key codes and stores nothing. Keys sent by programs (including Shift Happens' own Ctrl+C/Ctrl+V) are
/// ignored.</summary>
sealed class DoubleTapMonitor : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const uint LLKHF_INJECTED = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? lpModuleName);

    readonly HookProc _proc;  // kept in a field so the GC does not collect the callback
    IntPtr _hook;
    TapDetector _detector = new();
    uint[] _targets = Array.Empty<uint>();
    Action? _action;
    SynchronizationContext? _ui;

    public DoubleTapMonitor() => _proc = OnKey;

    public bool IsRunning => _hook != IntPtr.Zero;

    /// <summary>Starts watching; call on the UI thread (the hook runs on this thread's message loop).
    /// Returns false if Windows refused the hook.</summary>
    public bool Start(DoubleTapKey key, Action action)
    {
        Stop();
        _targets = key switch
        {
            DoubleTapKey.Ctrl => new uint[] { 0xA2, 0xA3 },   // VK_LCONTROL, VK_RCONTROL
            DoubleTapKey.Alt => new uint[] { 0xA4, 0xA5 },    // VK_LMENU, VK_RMENU
            _ => new uint[] { 0xA0, 0xA1 },                   // VK_LSHIFT, VK_RSHIFT
        };
        _action = action;
        _ui = SynchronizationContext.Current;
        _detector = new TapDetector();
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        return _hook != IntPtr.Zero;
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _action = null;
    }

    public void Dispose() => Stop();

    IntPtr OnKey(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((k.flags & LLKHF_INJECTED) == 0)
            {
                int msg = (int)wParam;
                bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool up = msg is WM_KEYUP or WM_SYSKEYUP;
                bool target = Array.IndexOf(_targets, k.vkCode) >= 0;
                TapDetector.Event? e = target ? (down ? TapDetector.Event.Down : up ? TapDetector.Event.Up : null)
                    : down ? TapDetector.Event.Other : null;
                // Run the action after the hook returns: the hook must answer fast, and the action sends keys.
                if (e is { } ev && _detector.Feed(ev, k.time / 1000.0) && _action is { } a)
                    _ui?.Post(_ => a(), null);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
