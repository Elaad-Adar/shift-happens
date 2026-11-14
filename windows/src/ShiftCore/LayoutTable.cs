using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShiftCore;

/// <summary>One physical key (with or without Shift) and the character it types on each layout.</summary>
public sealed record KeyEntry(
    [property: JsonPropertyName("key")] int Key,
    [property: JsonPropertyName("shift")] bool Shift,
    [property: JsonPropertyName("latin")] string Latin,
    [property: JsonPropertyName("hebrew")] string Hebrew);

/// <summary>The key-by-key table of two installed layouts, read from the OS (never typed by hand).
/// Same JSON shape as the Mac app's <c>fixtures/mac-abc-hebrew.json</c>.</summary>
public sealed class LayoutTable
{
    [JsonPropertyName("latinID")] public string LatinId { get; init; } = "";
    [JsonPropertyName("hebrewID")] public string HebrewId { get; init; } = "";
    [JsonPropertyName("entries")] public List<KeyEntry> Entries { get; init; } = new();

    /// <summary>Character maps both ways. When two keys type the same character, the unshifted key
    /// wins, then the lower key code.</summary>
    public (Dictionary<char, char> LatinToHebrew, Dictionary<char, char> HebrewToLatin) Maps()
    {
        var l2h = new Dictionary<char, char>();
        var h2l = new Dictionary<char, char>();
        foreach (var e in Entries.OrderBy(e => e.Shift).ThenBy(e => e.Key))
        {
            if (e.Latin.Length != 1 || e.Hebrew.Length != 1) continue;
            l2h.TryAdd(e.Latin[0], e.Hebrew[0]);
            h2l.TryAdd(e.Hebrew[0], e.Latin[0]);
        }
        return (l2h, h2l);
    }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static LayoutTable Load(string path) =>
        JsonSerializer.Deserialize<LayoutTable>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty layout table: {path}");

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}
