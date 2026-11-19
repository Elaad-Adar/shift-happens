using System.Text;
using ShiftCore;

namespace ShiftHappens;

sealed record LayoutPair(IntPtr Latin, IntPtr Hebrew, string LatinId, string HebrewId);

/// <summary>Reads the installed English and Hebrew keyboards and builds the key table with ToUnicodeEx.
/// The key in each entry is the physical scan code.</summary>
static class WinLayouts
{
    const ushort LangHebrew = 0x040D;
    const ushort LangEnglishUS = 0x0409;
    const ushort PrimaryEnglish = 0x09;

    static ushort LangId(IntPtr hkl) => (ushort)((long)hkl & 0xFFFF);

    static string Id(IntPtr hkl) => ((ulong)(long)hkl & 0xFFFFFFFF).ToString("X8");

    static IntPtr[] Installed()
    {
        int n = Native.GetKeyboardLayoutList(0, null);
        var list = new IntPtr[n];
        Native.GetKeyboardLayoutList(n, list);
        return list;
    }

    public static LayoutPair FindPair()
    {
        var list = Installed();
        var hebrew = list.FirstOrDefault(h => LangId(h) == LangHebrew);
        var latin = list.FirstOrDefault(h => LangId(h) == LangEnglishUS);
        if (latin == IntPtr.Zero) latin = list.FirstOrDefault(h => (LangId(h) & 0x3FF) == PrimaryEnglish);
        // English under a newer locale such as English (Israel) has no classic language id: Windows gives
        // it a placeholder (0x2000), so the checks above miss it (#29). Fall back to the first layout that
        // is not Hebrew and types Latin letters on the Q and A keys. Russian or Arabic fail this test.
        if (latin == IntPtr.Zero) latin = list.FirstOrDefault(h => LangId(h) != LangHebrew && TypesLatin(h));
        if (hebrew == IntPtr.Zero)
            throw new InvalidOperationException("No Hebrew keyboard. Add one in Settings > Time & language > Language & region.");
        if (latin == IntPtr.Zero)
            throw new InvalidOperationException("No English keyboard. Add one in Settings > Time & language > Language & region.");
        return new LayoutPair(latin, hebrew, Id(latin), Id(hebrew));
    }

    const uint ScanQ = 0x10;
    const uint ScanA = 0x1E;

    /// <summary>True when the layout types q and a on the Q and A keys, as US English and similar layouts do.</summary>
    static bool TypesLatin(IntPtr hkl) => Character(hkl, ScanQ, false) == "q" && Character(hkl, ScanA, false) == "a";

    /// <summary>Scan codes of the main typing block: digit row, three letter rows, space, and the extra
    /// key next to left Shift on ISO keyboards.</summary>
    static readonly uint[] TypingKeys =
        Enumerable.Range(0x02, 12)      // 1 .. =
            .Concat(Enumerable.Range(0x10, 12))  // Q .. ]
            .Concat(Enumerable.Range(0x1E, 12))  // A .. `
            .Concat(Enumerable.Range(0x2B, 11))  // \ .. /
            .Concat(new[] { 0x39, 0x56 })        // space, ISO extra key
            .Select(k => (uint)k).ToArray();

    /// <summary>Character typed by one physical key on one layout, or null.</summary>
    public static string? Character(IntPtr hkl, uint scanCode, bool shift)
    {
        uint vk = Native.MapVirtualKeyEx(scanCode, Native.MAPVK_VSC_TO_VK_EX, hkl);
        if (vk == 0) return null;
        var state = new byte[256];
        if (shift) state[Native.VK_SHIFT] = 0x80;
        var buf = new StringBuilder(8);
        // Flag 0x4: do not change the keyboard state (no dead-key side effects). Windows 10 1607+.
        int n = Native.ToUnicodeEx(vk, scanCode, state, buf, buf.Capacity, 0x4, hkl);
        if (n <= 0) return null;
        var s = buf.ToString(0, n);
        return s[0] < 0x20 || s[0] == 0x7F ? null : s;
    }

    public static LayoutTable Table(LayoutPair pair)
    {
        var entries = new List<KeyEntry>();
        foreach (var key in TypingKeys)
        {
            foreach (var shift in new[] { false, true })
            {
                var l = Character(pair.Latin, key, shift);
                var h = Character(pair.Hebrew, key, shift);
                if (l is null || h is null) continue;
                entries.Add(new KeyEntry((int)key, shift, l, h));
            }
        }
        return new LayoutTable { LatinId = pair.LatinId, HebrewId = pair.HebrewId, Entries = entries };
    }

    /// <summary>Asks the foreground window to switch to <paramref name="hkl"/>.</summary>
    public static void Activate(IntPtr hkl) =>
        Native.PostMessage(Native.GetForegroundWindow(), Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
}
