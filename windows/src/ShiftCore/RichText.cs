using System.Globalization;
using System.Text;

namespace ShiftCore;

/// <summary>Keeps formatting when Fix or Swap runs on rich text (#26). Apps such as browsers and Electron
/// editors put an HTML copy next to the plain text, and list numbers, bullets and bold live only in the
/// HTML. The converter works on the plain text; this carries its word changes into the HTML text, leaving
/// every tag as it was. Port of <c>Sources/ShiftCore/RichText.swift</c>.</summary>
public static class RichText
{
    static readonly HashSet<string> RawTextTags = new() { "script", "style", "title", "textarea" };

    static readonly Dictionary<string, string> Named = new()
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
    };

    /// <summary><paramref name="html"/> with each word of <paramref name="original"/> replaced by the word at
    /// the same place in <paramref name="converted"/>. Null when the HTML's text does not match
    /// <paramref name="original"/> word for word (for example a word split by a tag), so the caller pastes
    /// plain text as before.</summary>
    public static string? ConvertHtml(string html, string original, string converted)
    {
        var from = Words(original);
        var to = Words(converted);
        if (from.Count == 0 || from.Count != to.Count) return null;
        var output = new StringBuilder();
        int k = 0, i = 0;
        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    int c = html.IndexOf("-->", i, StringComparison.Ordinal);
                    int cEnd = c < 0 ? html.Length : c + 3;
                    output.Append(html, i, cEnd - i);
                    i = cEnd;
                    continue;
                }
                int gt = html.IndexOf('>', i);
                int end = gt < 0 ? html.Length : gt + 1;
                var tag = html.Substring(i, end - i);
                output.Append(tag);
                i = end;
                // Text inside these is not shown: copy it unchanged up to the closing tag.
                if (OpeningTagName(tag) is { } name && RawTextTags.Contains(name))
                {
                    int close = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                    if (close < 0) close = html.Length;
                    output.Append(html, i, close - i);
                    i = close;
                }
                continue;
            }
            int lt = html.IndexOf('<', i);
            int textEnd = lt < 0 ? html.Length : lt;
            var raw = html.Substring(i, textEnd - i);
            i = textEnd;
            bool changed = false;
            var text = new StringBuilder();
            foreach (var token in Tokens(DecodeEntities(raw)))
            {
                if (char.IsWhiteSpace(token[0]))
                {
                    text.Append(token);
                    continue;
                }
                if (k >= from.Count || token != from[k]) return null;
                if (to[k] != token) changed = true;
                text.Append(to[k]);
                k++;
            }
            output.Append(changed ? Encode(text.ToString()) : raw);
        }
        return k == from.Count ? output.ToString() : null;
    }

    internal static List<string> Words(string s) => Tokens(s).Where(t => !char.IsWhiteSpace(t[0])).ToList();

    /// <summary>Alternating runs of whitespace and non-whitespace, nothing dropped. Surrogate pairs are never
    /// whitespace, so they are never split.</summary>
    internal static List<string> Tokens(string s)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool currentIsSpace = false;
        foreach (char c in s)
        {
            bool space = char.IsWhiteSpace(c);
            if (current.Length > 0 && space != currentIsSpace)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            currentIsSpace = space;
            current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    static string? OpeningTagName(string tag)
    {
        if (tag.Length < 2 || !char.IsLetter(tag[1])) return null;
        int n = 1;
        while (n < tag.Length && char.IsLetterOrDigit(tag[n])) n++;
        return tag[1..n].ToLowerInvariant();
    }

    internal static string DecodeEntities(string s)
    {
        if (!s.Contains('&')) return s;
        var output = new StringBuilder();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '&')
            {
                int semi = s.IndexOf(';', i, Math.Min(12, s.Length - i));
                if (semi > i && Entity(s[(i + 1)..semi]) is { } value)
                {
                    output.Append(value);
                    i = semi + 1;
                    continue;
                }
            }
            output.Append(s[i]);
            i++;
        }
        return output.ToString();
    }

    static string? Entity(string name)
    {
        if (Named.TryGetValue(name, out var v)) return v;
        if (!name.StartsWith('#')) return null;
        var digits = name[1..];
        bool hex = digits.StartsWith('x') || digits.StartsWith('X');
        bool ok = hex
            ? int.TryParse(digits[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)
            : int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out code);
        if (!ok || code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) return null;
        return char.ConvertFromUtf32(code);
    }

    internal static string Encode(string s)
    {
        var output = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            output.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                ' ' => "&nbsp;",
                _ => c.ToString(),
            });
        }
        return output.ToString();
    }
}

/// <summary>Windows' "HTML Format" clipboard data: a header with UTF-8 byte offsets, then the HTML with the
/// copied part between StartFragment and EndFragment comments.</summary>
public static class CfHtml
{
    const string StartMarker = "<!--StartFragment-->";
    const string EndMarker = "<!--EndFragment-->";

    /// <summary>The copied part of a CF_HTML string, or null when it has no fragment markers.</summary>
    public static string? Fragment(string cfHtml)
    {
        int start = cfHtml.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += StartMarker.Length;
        int end = cfHtml.IndexOf(EndMarker, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? null : cfHtml[start..end];
    }

    /// <summary><paramref name="cfHtml"/> with its copied part replaced by <paramref name="fragment"/>. The HTML
    /// around the fragment (for example the <c>&lt;ol&gt;</c> of a copied <c>&lt;li&gt;</c>) is kept, and the
    /// header's byte offsets are recomputed. Null when there are no fragment markers.</summary>
    public static string? ReplaceFragment(string cfHtml, string fragment)
    {
        // The header is "Key:value" lines; the HTML starts at the first '<'.
        int htmlStart = cfHtml.IndexOf('<');
        if (htmlStart < 0) return null;
        var html = cfHtml[htmlStart..];
        int start = html.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += StartMarker.Length;
        int end = html.IndexOf(EndMarker, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        return Build(html[..start], fragment, html[end..]);
    }

    static string Build(string before, string fragment, string after)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        int headerBytes = Encoding.UTF8.GetByteCount(string.Format(CultureInfo.InvariantCulture, header, 0, 0, 0, 0));
        int startHtml = headerBytes;
        int startFragment = startHtml + Encoding.UTF8.GetByteCount(before);
        int endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        int endHtml = endFragment + Encoding.UTF8.GetByteCount(after);
        return string.Format(CultureInfo.InvariantCulture, header, startHtml, endHtml, startFragment, endFragment)
               + before + fragment + after;
    }
}
