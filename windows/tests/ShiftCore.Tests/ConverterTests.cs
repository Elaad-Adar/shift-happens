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
    static readonly HashSet<string> En = new() { "hello", "world", "the", "things", "i", "love", "what", "is", "this", "ok",
        "how", "are", "you", "it", "its", "to", "whats", "up" };
    static readonly HashSet<string> He = new() { "שלום", "מה", "נשמע", "אני", "אוהב", "ספר", "ים", "שרק", "אם" };

    static HashSet<string> Words(Lang lang) => lang == Lang.En ? En : He;

    public bool IsCorrect(string word, Lang lang) => Words(lang).Contains(word.ToLowerInvariant());

    public IReadOnlyList<string> Guesses(string word, Lang lang) =>
        Words(lang).Where(w => TextTools.Distance(word.ToLowerInvariant(), w) <= 2).Order(StringComparer.Ordinal).ToList();
}

/// <summary>Like the Windows en-US spell checker: for a word it does not know, it also suggests the same
/// word with a capital first letter ("vhh" -> "Vhh"), which must not count as a spelling fix (#30).</summary>
sealed class CapitalizingSpell : ISpellChecking
{
    readonly FakeSpell _base = new();

    public bool IsCorrect(string word, Lang lang) => _base.IsCorrect(word, lang) || (lang == Lang.He && word == "היי");

    public IReadOnlyList<string> Guesses(string word, Lang lang)
    {
        var g = _base.Guesses(word, lang).ToList();
        if (lang == Lang.En && !IsCorrect(word, Lang.En) && word.Length > 0)
            g.Insert(0, char.ToUpperInvariant(word[0]) + word[1..]);
        return g;
    }
}

/// <summary>A spell checker with fixed suggestions, for the cases real spell checkers give on short words.</summary>
sealed class GuessSpell(string[] guesses) : ISpellChecking
{
    readonly FakeSpell _base = new();

    public bool IsCorrect(string word, Lang lang) => _base.IsCorrect(word, lang) || (lang == Lang.He && word == "היי");

    public IReadOnlyList<string> Guesses(string word, Lang lang) =>
        lang == Lang.En && word == "vhh" ? guesses : _base.Guesses(word, lang);
}

/// <summary>Suggests the name "London" for "londn".</summary>
sealed class NameSpell : ISpellChecking
{
    public bool IsCorrect(string word, Lang lang) => false;
    public IReadOnlyList<string> Guesses(string word, Lang lang) => word == "londn" ? new[] { "London" } : Array.Empty<string>();
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
    // Sentence on the wrong layout whose words also read as short Hebrew words (#15).
    [InlineData("ים׳ שרק טםו", "how are you")]
    // Correct short sentences and single short words stay.
    [InlineData("is it ok", "is it ok")]
    [InlineData("אם", "אם")]
    [InlineData("to", "to")]
    // Contractions without the apostrophe (#14), even when the dictionary accepts them.
    [InlineData("whats", "what's")]
    [InlineData("Whats up", "What's up")]
    [InlineData("im", "I'm")]
    [InlineData("IM", "I'M")]
    [InlineData("its", "its")]
    public void Smart(string input, string expected) => Assert.Equal(expected, Make().Convert(input).Text);

    [Fact]
    public void CapitalOnlyGuessIsNotAFix()
    {
        var c = new Converter(Fixtures.MacTable(), new CapitalizingSpell());
        var r = c.Convert("vhh");
        Assert.Equal("היי", r.Text);
        Assert.Equal(Script.Hebrew, r.SwitchTo);
        // A real typo still gets its fix, not a capitalized copy of itself.
        Assert.Equal("the", c.Convert("teh").Text);
    }

    [Fact]
    public void NameIsNotAFixForLowercase()
    {
        // The Windows spell checker suggests the name "Vah" for "vhh". A lowercase word is not a
        // misspelled name, so the real Hebrew word "היי" after the swap wins.
        var c = new Converter(Fixtures.MacTable(), new GuessSpell(new[] { "Vah" }));
        var r = c.Convert("vhh");
        Assert.Equal("היי", r.Text);
        Assert.Equal(Script.Hebrew, r.SwitchTo);
        Assert.Equal("the", c.Convert("teh").Text);
        // A longer lowercase word may be a misspelled name.
        Assert.Equal("London", new Converter(Fixtures.MacTable(), new NameSpell()).Convert("londn").Text);
    }

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

    [Fact]
    public void ListNumbersStay()
    {
        // A numbered list copied as plain text: "1." is a list number, not a mistyped "1ץ".
        Assert.Equal("1. שלום\n2. שלום", Make().Convert("1. akuo\n2. akuo").Text);
        Assert.Equal("שלום 3.5", Make().Convert("akuo 3.5").Text);
        // Real punctuation next to a number still swaps: the / key types "." on Hebrew.
        Assert.Equal("שלום 3.", Make().Convert("akuo 3/").Text);
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
