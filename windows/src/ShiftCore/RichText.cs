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

    /// <summary>Named entities that copied HTML uses for text (Word, Outlook and browsers), including the
    /// direction marks common in Hebrew mail. An unknown entity does not match the plain text, so the paste
    /// falls back to plain text.</summary>
    static readonly Dictionary<string, string> Named = new()
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"",
        ["apos"] = "'", ["nbsp"] = "\u00A0", ["lrm"] = "\u200E", ["rlm"] = "\u200F",
        ["zwj"] = "\u200D", ["zwnj"] = "\u200C", ["shy"] = "\u00AD", ["lsquo"] = "\u2018",
        ["rsquo"] = "\u2019", ["sbquo"] = "\u201A", ["ldquo"] = "\u201C", ["rdquo"] = "\u201D",
        ["bdquo"] = "\u201E", ["ndash"] = "\u2013", ["mdash"] = "\u2014", ["hellip"] = "\u2026",
        ["bull"] = "\u2022", ["middot"] = "\u00B7", ["copy"] = "\u00A9", ["reg"] = "\u00AE",
        ["trade"] = "\u2122", ["euro"] = "\u20AC", ["shekel"] = "\u20AA", ["deg"] = "\u00B0",
        ["times"] = "\u00D7", ["laquo"] = "\u00AB", ["raquo"] = "\u00BB", ["ensp"] = "\u2002",
        ["emsp"] = "\u2003", ["thinsp"] = "\u2009",
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
                int end = TagEnd(html, i);
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
                // List numbers and bullets ("1.", "•") are in the plain text but drawn by the page, so the
                // HTML text lacks them. Skip such a plain-text word when it has no letters and stays as is.
                while (k < from.Count && token != from[k] && IsMarker(from[k], to[k])) k++;
                if (k >= from.Count || token != from[k]) return null;
                if (to[k] != token) changed = true;
                text.Append(to[k]);
                k++;
            }
            output.Append(changed ? Encode(text.ToString()) : raw);
        }
        while (k < from.Count && IsMarker(from[k], to[k])) k++;
        return k == from.Count ? output.ToString() : null;
    }

    /// <summary>A plain-text word the HTML may leave out: no letters, and not changed by the conversion.</summary>
    internal static bool IsMarker(string from, string to) => from == to && !from.Any(char.IsLetter);

    /// <summary>The index just past the <c>&gt;</c> that closes the tag starting at <paramref name="start"/>.
    /// A <c>&gt;</c> inside a quoted attribute value (<c>title="a&gt;b"</c>) does not close it.</summary>
    static int TagEnd(string html, int start)
    {
        char quote = '\0';
        for (int i = start + 1; i < html.Length; i++)
        {
            char c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c == '"' || c == '\'') quote = c;
            else if (c == '>') return i + 1;
        }
        return html.Length;
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
        int htmlStart = HtmlStart(cfHtml);
        if (htmlStart < 0) return null;
        var html = cfHtml[htmlStart..];
        int start = html.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += StartMarker.Length;
        int end = html.IndexOf(EndMarker, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        return Build(html[..start], fragment, html[end..]);
    }

    /// <summary>Where the HTML starts: the header's StartHTML byte offset when it is valid, else the first '&lt;'
    /// (the header is "Key:value" lines).</summary>
    static int HtmlStart(string cfHtml)
    {
        const string key = "StartHTML:";
        int at = cfHtml.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            int n = at + key.Length;
            int stop = n;
            while (stop < cfHtml.Length && char.IsAsciiDigit(cfHtml[stop])) stop++;
            if (stop > n && int.TryParse(cfHtml.AsSpan(n, stop - n), NumberStyles.None, CultureInfo.InvariantCulture, out int offset))
            {
                var bytes = Encoding.UTF8.GetBytes(cfHtml);
                if (offset > 0 && offset < bytes.Length && bytes[offset] == (byte)'<')
                    return Encoding.UTF8.GetCharCount(bytes, 0, offset);
            }
        }
        return cfHtml.IndexOf('<');
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
