using System.Text;
using ShiftCore;

namespace ShiftCore.Tests;

/// <summary>Same cases as <c>Tests/ShiftCoreTests/RichTextTests.swift</c>, plus the Windows CF_HTML wrapper.</summary>
public class RichTextTests
{
    [Fact]
    public void ListKeepsItsTags() =>
        Assert.Equal("<meta charset='utf-8'><ol><li>שלום</li>\n<li>שלום שלום</li></ol>",
            RichText.ConvertHtml("<meta charset='utf-8'><ol><li>akuo</li>\n<li>akuo akuo</li></ol>",
                "akuo\nakuo akuo", "שלום\nשלום שלום"));

    [Fact]
    public void UnchangedTextStaysByteForByte() =>
        Assert.Equal("<p>x &amp; y</p><p><b>שלום</b></p>",
            RichText.ConvertHtml("<p>x &amp; y</p><p><b>akuo</b></p>", "x & y\nakuo", "x & y\nשלום"));

    [Fact]
    public void ChangedTextIsEncoded() =>
        Assert.Equal("<p>c&nbsp;&lt;d</p>", RichText.ConvertHtml("<p>a&nbsp;&lt;b</p>", "a <b", "c <d"));

    [Fact]
    public void NumericEntities() =>
        Assert.Equal("<p>a b</p>", RichText.ConvertHtml("<p>&#1513;x &#x5e9;y</p>", "שx שy", "a b"));

    [Fact]
    public void StyleScriptAndCommentsAreSkipped() =>
        Assert.Equal("<style>p { color: red }</style><!--StartFragment--><p>שלום</p><!--EndFragment-->",
            RichText.ConvertHtml("<style>p { color: red }</style><!--StartFragment--><p>akuo</p><!--EndFragment-->",
                "akuo", "שלום"));

    [Fact]
    public void QuotedGreaterThanInsideATag() =>
        Assert.Equal("<a title=\"a>b\" href='x>y'>שלום</a>",
            RichText.ConvertHtml("<a title=\"a>b\" href='x>y'>akuo</a>", "akuo", "שלום"));

    [Fact]
    public void WordAndOutlookEntities() =>
        Assert.Equal("<p>don\u2019t\u2026 \u200Fשלום</p>",
            RichText.ConvertHtml("<p>don&rsquo;t&hellip; &rlm;akuo</p>", "don\u2019t\u2026 \u200Fakuo", "don\u2019t\u2026 \u200Fשלום"));

    [Fact]
    public void CfHtmlUsesStartHtmlOffsetNotFirstAngleBracket()
    {
        // A header field with '<' before the HTML: the StartHTML offset says where the HTML really starts.
        var html = "<html><body><!--StartFragment--><p>akuo</p><!--EndFragment--></body></html>";
        var head = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\n" +
                   "EndFragment:0000000000\r\nSourceURL:https://example.com/?q=<x>\r\n";
        int headBytes = Encoding.UTF8.GetByteCount(string.Format(head, 0));
        var original = string.Format(head, headBytes) + html;
        var cf = CfHtml.ReplaceFragment(original, "<p>שלום</p>");
        Assert.NotNull(cf);
        // Searching for the first '<' would have copied "<x>" from the header into the HTML.
        Assert.DoesNotContain("<x>", cf);
        Assert.EndsWith("<html><body><!--StartFragment--><p>שלום</p><!--EndFragment--></body></html>", cf);
    }

    [Fact]
    public void WordSplitByATagFallsBack() =>
        Assert.Null(RichText.ConvertHtml("<p>ak<b>uo</b></p>", "akuo", "שלום"));

    [Fact]
    public void TextThatDoesNotMatchFallsBack()
    {
        Assert.Null(RichText.ConvertHtml("<p>akuo extra</p>", "akuo", "שלום"));
        Assert.Null(RichText.ConvertHtml("<p>akuo</p>", "akuo akuo", "שלום שלום"));
        Assert.Null(RichText.ConvertHtml("<p>akuo</p>", "akuo", "שלום שלום"));
    }

    /// <summary>Byte offsets from a CF_HTML header.</summary>
    static int Field(string cf, string name)
    {
        int at = cf.IndexOf(name + ":", StringComparison.Ordinal) + name.Length + 1;
        return int.Parse(cf.Substring(at, 10));
    }

    [Fact]
    public void CfHtmlKeepsContextAndFixesOffsets()
    {
        // Microsoft's example layout: the list's <ol> is outside the fragment markers.
        var original = "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000199\r\n" +
                       "StartFragment:0000000141\r\nEndFragment:0000000163\r\nSourceURL:https://example.com/\r\n" +
                       "<html><body><ol><!--StartFragment--><li>akuo</li><!--EndFragment--></ol></body></html>";
        var fragment = CfHtml.Fragment(original);
        Assert.Equal("<li>akuo</li>", fragment);
        var rich = RichText.ConvertHtml(fragment!, "akuo", "שלום");
        var cf = CfHtml.ReplaceFragment(original, rich!);
        Assert.NotNull(cf);
        Assert.EndsWith("<html><body><ol><!--StartFragment--><li>שלום</li><!--EndFragment--></ol></body></html>", cf);

        var bytes = Encoding.UTF8.GetBytes(cf!);
        int start = Field(cf!, "StartFragment"), end = Field(cf!, "EndFragment");
        Assert.Equal("<li>שלום</li>", Encoding.UTF8.GetString(bytes, start, end - start));
        Assert.Equal(bytes.Length, Field(cf!, "EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Field(cf!, "StartHTML"), 6));
        Assert.Equal("<li>שלום</li>", CfHtml.Fragment(cf!));
    }

    [Fact]
    public void CfHtmlWithoutMarkersCannotBeReplaced() =>
        Assert.Null(CfHtml.ReplaceFragment("Version:0.9\r\n<p>x</p>", "<p>y</p>"));

    [Fact]
    public void CfHtmlWithoutMarkersHasNoFragment() => Assert.Null(CfHtml.Fragment("Version:0.9\r\n<p>x</p>"));
}
