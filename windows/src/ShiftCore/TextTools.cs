using System.Text;

namespace ShiftCore;

public enum Script { Latin, Hebrew }

public enum Lang { En, He }

public static class ScriptExtensions
{
    public static Script Other(this Script s) => s == Script.Latin ? Script.Hebrew : Script.Latin;
    public static Lang Lang(this Script s) => s == Script.Latin ? ShiftCore.Lang.En : ShiftCore.Lang.He;
}

public static class TextTools
{
    public static bool IsHebrewLetter(char c) => c >= 'א' && c <= 'ת';

    public static bool IsLatinLetter(char c) => char.IsLetter(c) && c < 'ɐ';

    /// <summary>Alternating runs of whitespace and non-whitespace, keeping every character.</summary>
    public static List<string> Runs(string s)
    {
        var runs = new List<string>();
        var cur = new StringBuilder();
        bool? curIsSpace = null;
        foreach (var c in s)
        {
            var sp = char.IsWhiteSpace(c);
            if (curIsSpace is bool b && b != sp)
            {
                runs.Add(cur.ToString());
                cur.Clear();
            }
            cur.Append(c);
            curIsSpace = sp;
        }
        if (cur.Length > 0) runs.Add(cur.ToString());
        return runs;
    }

    /// <summary>Leading punctuation, the letter core, trailing punctuation.</summary>
    public static (string Lead, string Core, string Trail) SplitCore(string t)
    {
        int first = -1, last = -1;
        for (int i = 0; i < t.Length; i++)
        {
            if (!char.IsLetter(t[i])) continue;
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0) return (t, "", "");
        return (t[..first], t[first..(last + 1)], t[(last + 1)..]);
    }

    /// <summary>Hebrew or Latin by majority of letters; null when there are none of either.</summary>
    public static Script? ScriptOf(string s)
    {
        int he = 0, la = 0;
        foreach (var c in s)
        {
            if (IsHebrewLetter(c)) he++;
            else if (IsLatinLetter(c)) la++;
        }
        if (he == 0 && la == 0) return null;
        return he > la ? Script.Hebrew : Script.Latin;
    }

    static readonly Dictionary<char, char> FinalToRegular = new()
    {
        ['ך'] = 'כ', ['ם'] = 'מ', ['ן'] = 'נ', ['ף'] = 'פ', ['ץ'] = 'צ',
    };

    static readonly Dictionary<char, char> RegularToFinal =
        FinalToRegular.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Lowercase, Hebrew final letters folded to regular form. Used only for distance.</summary>
    public static string Fold(string s) =>
        new(s.ToLowerInvariant().Select(c => FinalToRegular.GetValueOrDefault(c, c)).ToArray());

    /// <summary>Final form at the end of a Hebrew word, regular form inside it.</summary>
    public static string FixHebrewFinals(string w)
    {
        if (w.Length == 0) return w;
        var chars = w.ToCharArray();
        for (int i = 0; i < chars.Length - 1; i++)
            chars[i] = FinalToRegular.GetValueOrDefault(chars[i], chars[i]);
        chars[^1] = RegularToFinal.GetValueOrDefault(chars[^1], chars[^1]);
        return new string(chars);
    }

    /// <summary>Optimal string alignment distance (Levenshtein plus adjacent swaps).</summary>
    public static int Distance(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }
        return d[n, m];
    }

    /// <summary>Gives <paramref name="word"/> the casing of <paramref name="like"/> (ALL CAPS or Capitalized).</summary>
    public static string MatchCase(string word, string like)
    {
        if (like.Length > 1 && like == like.ToUpperInvariant() && like != like.ToLowerInvariant())
            return word.ToUpperInvariant();
        if (like.Length > 0 && char.IsUpper(like[0]) && word.Length > 0)
            return char.ToUpperInvariant(word[0]) + word[1..];
        return word;
    }
}
