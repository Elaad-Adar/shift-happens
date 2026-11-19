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
    public void WordSplitByATagFallsBack() =>
        Assert.Null(RichText.ConvertHtml("<p>ak<b>uo</b></p>", "akuo", "שלום"));

    [Fact]
    public void TextThatDoesNotMatchFallsBack()
    {
        Assert.Null(RichText.ConvertHtml("<p>akuo extra</p>", "akuo", "שלום"));
        Assert.Null(RichText.ConvertHtml("<p>akuo</p>", "akuo akuo", "שלום שלום"));
        Assert.Null(RichText.ConvertHtml("<p>akuo</p>", "akuo", "שלום שלום"));
    }

    [Fact]
    public void CfHtmlOffsetsPointAtTheFragment()
    {
        var fragment = "<ol><li>שלום</li></ol>";
        var cf = CfHtml.Wrap(fragment);
        var bytes = Encoding.UTF8.GetBytes(cf);
        int Field(string name)
        {
            int at = cf.IndexOf(name + ":", StringComparison.Ordinal) + name.Length + 1;
            return int.Parse(cf.Substring(at, 10));
        }
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes, Field("StartFragment"), Field("EndFragment") - Field("StartFragment")));
        Assert.Equal(bytes.Length, Field("EndHTML"));
        Assert.StartsWith("<html>", Encoding.UTF8.GetString(bytes, Field("StartHTML"), 6));
        Assert.Equal(fragment, CfHtml.Fragment(cf));
    }

    [Fact]
    public void CfHtmlWithoutMarkersHasNoFragment() => Assert.Null(CfHtml.Fragment("Version:0.9\r\n<p>x</p>"));
}
