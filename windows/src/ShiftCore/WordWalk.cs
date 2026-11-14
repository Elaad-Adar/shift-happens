namespace ShiftCore;

/// <summary>
/// Port of the Mac app's <c>Sources/ShiftCore/WordWalk.swift</c>; keep the two in step.
/// Finds the word just before the cursor when nothing is selected, by extending the selection one
/// character left at a time (Shift+Left) and reading it back with a copy after each step.
/// Character steps, not word steps: a word typed on the wrong layout contains punctuation that word
/// jumps stop at (<c>ba,g</c> is נשמע typed on English), so Ctrl+Shift+Left would grab only <c>g</c>.
/// </summary>
public sealed class WordWalk
{
    public enum Step
    {
        /// <summary>Press Shift+Left once more, then copy and call <see cref="Next"/> again.</summary>
        Extend,
        /// <summary>The word is selected; copy the final selection.</summary>
        Done,
        /// <summary>Press Shift+Right once to drop the whitespace (space, tab, line break) the last
        /// step added, then copy the final selection.</summary>
        DoneShrink,
        /// <summary>No word before the cursor (start of field, only whitespace, or longer than the cap).</summary>
        Fail,
    }

    public int MaxCharacters { get; }
    string _last = "";
    int _steps;

    public WordWalk(int maxCharacters = 40) => MaxCharacters = maxCharacters;

    /// <summary><paramref name="selection"/>: the text copied after the latest Shift+Left (pass the
    /// previous text again when the copy did not change the clipboard).</summary>
    public Step Next(string selection)
    {
        _steps++;
        var last = _last;
        _last = selection;
        bool hasWord = selection.Any(c => !char.IsWhiteSpace(c));

        if (selection == last) return hasWord ? Step.Done : Step.Fail;
        if (!hasWord) return _steps > MaxCharacters ? Step.Fail : Step.Extend;
        if (char.IsWhiteSpace(selection[0])) return Step.DoneShrink;
        return selection.Length > MaxCharacters ? Step.Fail : Step.Extend;
    }
}
