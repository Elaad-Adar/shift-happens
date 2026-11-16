namespace ShiftCore;

/// <summary>English contractions typed without the apostrophe. Spell checkers often accept these ("whats",
/// "cant"), so Fix would leave them alone. Only words that are not real words on their own are listed:
/// "its", "lets", "wont", "ill", "well", "were", "shell", "hell", "id", "wed", "shed", "hes" stay out.</summary>
public static class Contractions
{
    static readonly Dictionary<string, string> Table = new()
    {
        ["whats"] = "what's", ["thats"] = "that's", ["dont"] = "don't", ["doesnt"] = "doesn't", ["didnt"] = "didn't",
        ["isnt"] = "isn't", ["arent"] = "aren't", ["wasnt"] = "wasn't", ["werent"] = "weren't", ["cant"] = "can't",
        ["couldnt"] = "couldn't", ["shouldnt"] = "shouldn't", ["wouldnt"] = "wouldn't", ["hasnt"] = "hasn't",
        ["havent"] = "haven't", ["hadnt"] = "hadn't", ["im"] = "I'm", ["ive"] = "I've", ["youre"] = "you're",
        ["theyre"] = "they're", ["theres"] = "there's", ["wheres"] = "where's", ["whos"] = "who's", ["youve"] = "you've",
        ["weve"] = "we've", ["theyve"] = "they've", ["youll"] = "you'll", ["theyll"] = "they'll", ["itll"] = "it'll",
        ["couldve"] = "could've", ["shouldve"] = "should've", ["wouldve"] = "would've",
    };

    /// <summary>The contraction for <paramref name="word"/> with its casing, or null. "im" always becomes "I'm".</summary>
    public static string? Fix(string word)
    {
        if (!Table.TryGetValue(word.ToLowerInvariant(), out var c)) return null;
        if (c.StartsWith("I'"))
            return word.Length > 1 && word == word.ToUpperInvariant() ? c.ToUpperInvariant() : c;
        return TextTools.MatchCase(c, word);
    }
}
