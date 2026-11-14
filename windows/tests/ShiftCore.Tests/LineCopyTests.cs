using System.Text;
using ShiftCore;

namespace ShiftCore.Tests;

public class LineCopyTests
{
    static byte[] Utf16(string s) => Encoding.Unicode.GetBytes(s);

    [Fact]
    public void VsCodeEmptySelectionCopy()
    {
        var blob = new byte[] { 0x38, 0x01, 0, 0 }
            .Concat(Utf16("vscode-editor-data"))
            .Concat(Utf16("{\"version\":1,\"isFromEmptySelection\":true,\"isBlock\":false}")).ToArray();
        Assert.True(LineCopy.IsFromEmptySelection(blob));
    }

    [Fact]
    public void RealSelectionCopy()
    {
        var blob = Utf16("vscode-editor-data").Concat(Utf16("{\"isFromEmptySelection\":false}")).ToArray();
        Assert.False(LineCopy.IsFromEmptySelection(blob));
        Assert.False(LineCopy.IsFromEmptySelection(Array.Empty<byte>()));
    }
}
