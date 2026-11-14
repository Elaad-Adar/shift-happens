using System.Text;

namespace ShiftCore;

/// <summary>Port of the Mac app's <c>Sources/ShiftCore/LineCopy.swift</c>. Code editors copy the whole
/// current line when nothing is selected; VS Code marks that copy with <c>"isFromEmptySelection":true</c>
/// (UTF-16) in Chromium's custom clipboard data.</summary>
public static class LineCopy
{
    public const string WindowsClipboardFormat = "Chromium Web Custom MIME Data Format";

    static readonly byte[] Marker = Encoding.ASCII.GetBytes("\"isFromEmptySelection\":true");

    /// <summary>True when <paramref name="data"/> says the copy came from an empty selection. Zero bytes
    /// are dropped first, which turns the UTF-16LE ASCII text into plain ASCII.</summary>
    public static bool IsFromEmptySelection(byte[] data)
    {
        var ascii = data.Where(b => b != 0).ToArray();
        return ascii.AsSpan().IndexOf(Marker) >= 0;
    }
}
