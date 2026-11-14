using System.Text.Json;

namespace ShiftCore;

/// <summary>Port of the Mac app's <c>Sources/ShiftCore/Releases.swift</c>: release and issue links for
/// this app's GitHub repo, and version comparison for the update notice.</summary>
public static class Releases
{
    public const string Repo = "Elaad-Adar/shift-happens";
    public static readonly Uri LatestApi = new($"https://api.github.com/repos/{Repo}/releases/latest");
    public static readonly Uri ReleasesPage = new($"https://github.com/{Repo}/releases");

    public static Uri NotesUrl(string version) => new($"https://github.com/{Repo}/releases/tag/v{version}");

    /// <summary>"v1.10.2" or "1.10.2-beta" -> [1, 10, 2]; null when there is no leading number.</summary>
    public static int[]? Parse(string version)
    {
        var s = version.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var parts = new List<int>();
        foreach (var piece in s.Split('.'))
        {
            var digits = new string(piece.TakeWhile(char.IsAsciiDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out var n)) break;
            parts.Add(n);
            if (digits.Length != piece.Length) break;
        }
        return parts.Count == 0 ? null : parts.ToArray();
    }

    /// <summary>True when <paramref name="candidate"/> is a higher version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        if (Parse(candidate) is not { } a || Parse(current) is not { } b) return false;
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    public sealed record Latest(string Version, Uri Url);

    /// <summary>The GitHub API answer for <c>releases/latest</c>: <c>tag_name</c> and <c>html_url</c>.</summary>
    public static Latest? DecodeLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tag) || !root.TryGetProperty("html_url", out var url)) return null;
            var t = tag.GetString() ?? "";
            if (Parse(t) is null || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var u)) return null;
            return new Latest(t.StartsWith('v') ? t[1..] : t, u);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public enum IssueKind { Bug, Idea }

    /// <summary>New-issue page with the matching form and the app version and OS filled in.</summary>
    public static Uri IssueUrl(IssueKind kind, string version, string os)
    {
        var template = kind == IssueKind.Bug ? "bug.yml" : "idea.yml";
        return new Uri($"https://github.com/{Repo}/issues/new?template={template}" +
                       $"&version={Uri.EscapeDataString(version)}&os={Uri.EscapeDataString(os)}");
    }
}
