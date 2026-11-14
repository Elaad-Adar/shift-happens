using ShiftCore;

namespace ShiftCore.Tests;

/// <summary>Same cases as the Swift <c>WordWalkTests</c>. Simulates a text field with the cursor at the
/// end; each Shift+Left grows the selection by one character (one "\r\n" counts as one step, as in Windows).</summary>
public class WordWalkTests
{
    static string? Walk(string text, int max = 40)
    {
        // Text elements: "\r\n" is one step, like the caret in a Windows edit control.
        var units = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { units.Add("\r\n"); i++; }
            else units.Add(text[i].ToString());
        }
        var w = new WordWalk(max);
        int length = 0;
        while (true)
        {
            length = Math.Min(length + 1, units.Count);
            var selection = string.Concat(units.Skip(units.Count - length));
            switch (w.Next(selection))
            {
                case WordWalk.Step.Extend:
                    continue;
                case WordWalk.Step.DoneShrink:
                    length--;
                    return string.Concat(units.Skip(units.Count - length));
                case WordWalk.Step.Done:
                    return selection;
                default:
                    return null;
            }
        }
    }

    [Theory]
    [InlineData("akuo", "akuo")]
    [InlineData("hello akuo", "akuo")]
    [InlineData("hello akuo ", "akuo ")]
    [InlineData("hello ba,g", "ba,g")]
    [InlineData("line one\nnv", "nv")]
    [InlineData("line one\r\nnv", "nv")]
    [InlineData("tab\tשלומ", "שלומ")]
    [InlineData("x  akuo  ", "akuo  ")]
    public void FindsWord(string text, string expected) => Assert.Equal(expected, Walk(text));

    [Fact]
    public void EmptyOrOnlySpacesFails()
    {
        Assert.Null(Walk(""));
        Assert.Null(Walk("   "));
    }

    [Fact]
    public void TooLongFails()
    {
        Assert.Null(Walk(new string('a', 41)));
        Assert.Equal(new string('a', 40), Walk(new string('a', 40)));
    }
}
