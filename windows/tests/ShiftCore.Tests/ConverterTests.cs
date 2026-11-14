using ShiftCore;

namespace ShiftCore.Tests;

/// <summary>Same cases as the Swift <c>Tests/ShiftCoreTests</c>, on the same Mac fixture table and the same
/// fake dictionary, so the C# port must give the same answers as the Mac app.</summary>
public static class Fixtures
{
    static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    public static LayoutTable MacTable() => LayoutTable.Load(Path.Combine(RepoRoot(), "fixtures", "mac-abc-hebrew.json"));
}

sealed class FakeSpell : ISpellChecking
{
    static readonly HashSet<string> En = new() { "hello", "world", "the", "things", "i", "love", "what", "is", "this", "ok" };
    static readonly HashSet<string> He = new() { "שלום", "מה", "נשמע", "אני", "אוהב", "ספר" };

    static HashSet<string> Words(Lang lang) => lang == Lang.En ? En : He;

    public bool IsCorrect(string word, Lang lang) => Words(lang).Contains(word.ToLowerInvariant());

    public IReadOnlyList<string> Guesses(string word, Lang lang) =>
        Words(lang).Where(w => TextTools.Distance(word.ToLowerInvariant(), w) <= 2).Order(StringComparer.Ordinal).ToList();
}

public class LayoutTableTests
{
    [Fact]
    public void MapsAreKeyForKey()
    {
        var (l2h, h2l) = Fixtures.MacTable().Maps();
        Assert.Equal('ש', l2h['a']);
        Assert.Equal('א', l2h['t']);
        Assert.Equal('ת', l2h[',']);
        Assert.Equal('a', h2l['ש']);
        Assert.Equal('o', h2l['ם']);
        Assert.Equal('n', h2l['מ']);
        Assert.Equal('1', l2h['1']);
    }

    [Fact]
    public void JsonRoundTrips()
    {
        var t = Fixtures.MacTable();
        var path = Path.GetTempFileName();
        File.WriteAllText(path, t.ToJson());
        var back = LayoutTable.Load(path);
        Assert.Equal(t.Entries, back.Entries);
        Assert.Equal(t.LatinId, back.LatinId);
    }
}

public class ConverterTests
{
    static Converter Make() => new(Fixtures.MacTable(), new FakeSpell());

    [Theory]
    [InlineData("akuo", "שלום")]
    [InlineData("יקךךם", "hello")]
    [InlineData("I love איןמעד", "I love things")]
    [InlineData("teh", "the")]
    [InlineData("Teh", "The")]
    [InlineData("שלומ", "שלום")]
    [InlineData("hello world", "hello world")]
    [InlineData("מה נשמע", "מה נשמע")]
    [InlineData("123", "123")]
    [InlineData("", "")]
    [InlineData("  akuo  ", "  שלום  ")]
    [InlineData("hello akuo", "hello שלום")]
    public void Smart(string input, string expected) => Assert.Equal(expected, Make().Convert(input).Text);

    [Fact]
    public void SwitchTarget()
    {
        var c = Make();
        Assert.Equal(Script.Hebrew, c.Convert("akuo").SwitchTo);
        Assert.Equal(Script.Latin, c.Convert("יקךךם").SwitchTo);
        Assert.Null(c.Convert("teh").SwitchTo);
        Assert.False(c.Convert("hello").Changed);
    }

    [Fact]
    public void ForceSwap()
    {
        var c = Make();
        Assert.Equal("יקךךם", c.Convert("hello", Mode.ForceSwap).Text);
        Assert.Equal("akuo", c.Convert("שלום", Mode.ForceSwap).Text);
    }

    [Fact]
    public void PunctuationOnlyRunsFollowFullSwap()
    {
        Assert.Equal("שלום ץ", Make().Convert("akuo .").Text);
        Assert.Equal("hello שלום .", Make().Convert("hello akuo .").Text);
    }
}

public class TextToolsTests
{
    [Fact]
    public void Distance()
    {
        Assert.Equal(1, TextTools.Distance("teh", "the"));
        Assert.Equal(1, TextTools.Distance("wrold", "world"));
        Assert.Equal(0, TextTools.Distance("abc", "abc"));
    }

    [Fact]
    public void HebrewFinals()
    {
        Assert.Equal("שלום", TextTools.FixHebrewFinals("שלומ"));
        Assert.Equal("מלך", TextTools.FixHebrewFinals("םלך"));
    }

    [Fact]
    public void RunsKeepEverything()
    {
        Assert.Equal("a  b\nc", string.Concat(TextTools.Runs("a  b\nc")));
        Assert.Equal(3, TextTools.Runs("a  b").Count);
    }
}
