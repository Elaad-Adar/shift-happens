namespace ShiftCore;

public interface ISpellChecking
{
    bool IsCorrect(string word, Lang lang);

    /// <summary>Suggestions, best first. May be empty.</summary>
    IReadOnlyList<string> Guesses(string word, Lang lang);
}

public enum Mode
{
    /// <summary>Wrong layout: swap. Right layout: fix spelling.</summary>
    Smart,
    /// <summary>Swap every letter word, no dictionary check.</summary>
    ForceSwap,
}

public sealed record Conversion(string Text, Script? SwitchTo, int SwappedWords, int FixedWords, IReadOnlyList<string> Notes)
{
    public bool Changed => SwappedWords > 0 || FixedWords > 0;
}

/// <summary>
/// Port of the Mac app's <c>Sources/ShiftCore/Converter.swift</c>; keep the two in step.
/// Decides per script group (all Hebrew words, all Latin words) whether the group was typed on the wrong
/// layout (swap it) or the right one (fix misspelled words). Words of one script were typed on one layout,
/// so they share one decision; a 4+ letter word that clearly reads right the other way is the exception.
/// </summary>
public sealed class Converter
{
    readonly Dictionary<char, char> _latinToHebrew;
    readonly Dictionary<char, char> _hebrewToLatin;
    readonly ISpellChecking _spell;

    public Converter(LayoutTable table, ISpellChecking spell)
    {
        (_latinToHebrew, _hebrewToLatin) = table.Maps();
        _spell = spell;
    }

    /// <summary>Key-for-key swap of <paramref name="s"/>, read as typed on the layout of <paramref name="from"/>.</summary>
    public string Swap(string s, Script from)
    {
        var map = from == Script.Latin ? _latinToHebrew : _hebrewToLatin;
        return new string(s.Select(c => map.GetValueOrDefault(c, c)).ToArray());
    }

    sealed record Word(int Index, Script Script, string Core, string Swapped, string SwappedCore, bool OrigOk, bool SwapOk,
        string? Contraction);

    enum Decision { Swap, Fix }

    public Conversion Convert(string text, Mode mode = Mode.Smart)
    {
        var runs = TextTools.Runs(text);
        var words = new List<Word>();
        var neutral = new List<int>();
        var notes = new List<string>();

        for (int i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.Length == 0 || char.IsWhiteSpace(run[0])) continue;
            var core = TextTools.SplitCore(run).Core;
            if (TextTools.ScriptOf(core) is not Script script)
            {
                neutral.Add(i);
                continue;
            }
            var swapped = Swap(run, script);
            var swappedCore = TextTools.SplitCore(swapped).Core;
            // "whats" -> "what's": typed on the right layout, missing only the apostrophe.
            var contraction = mode == Mode.ForceSwap || script != Script.Latin ? null : Contractions.Fix(core);
            bool origOk = mode != Mode.ForceSwap && (contraction is not null || _spell.IsCorrect(core, script.Lang()));
            bool swapOk = mode != Mode.ForceSwap && swappedCore.Length > 0
                && TextTools.ScriptOf(swappedCore) == script.Other()
                && _spell.IsCorrect(swappedCore, script.Other().Lang());
            words.Add(new Word(i, script, core, swapped, swappedCore, origOk, swapOk, contraction));
        }

        var swappedCount = new Dictionary<Script, int>();
        int fixedCount = 0;

        foreach (var script in new[] { Script.Latin, Script.Hebrew })
        {
            var group = words.Where(w => w.Script == script).ToList();
            if (group.Count == 0) continue;
            var decision = Decide(group, mode, notes);
            foreach (var w in group)
            {
                bool strongSwap = w.SwapOk && !w.OrigOk && w.SwappedCore.Length >= 4;
                bool strongKeep = w.OrigOk && !w.SwapOk && w.Core.Length >= 4;
                bool shouldSwap = decision == Decision.Swap
                    ? !strongKeep
                    : strongSwap && CloseGuess(w.Core, script.Lang()) is null;
                if (shouldSwap)
                {
                    runs[w.Index] = w.Swapped;
                    swappedCount[script] = swappedCount.GetValueOrDefault(script) + 1;
                    notes.Add($"swap  {w.Core} -> {w.SwappedCore}");
                }
                else if (w.Contraction is string c)
                {
                    var (lead, _, trail) = TextTools.SplitCore(runs[w.Index]);
                    runs[w.Index] = lead + c + trail;
                    fixedCount++;
                    notes.Add($"fix   {w.Core} -> {c}");
                }
                else if (w.OrigOk)
                {
                    continue;
                }
                else if (CloseGuess(w.Core, script.Lang()) is string g)
                {
                    var (lead, _, trail) = TextTools.SplitCore(runs[w.Index]);
                    runs[w.Index] = lead + g + trail;
                    fixedCount++;
                    notes.Add($"fix   {w.Core} -> {g}");
                }
                else
                {
                    notes.Add($"keep  {w.Core} (no close guess)");
                }
            }
        }

        // Punctuation-only runs follow the swap only when every word was swapped,
        // i.e. the whole selection was typed on the wrong layout.
        Script? target = null;
        int total = swappedCount.Values.Sum();
        if (total > 0)
        {
            var from = swappedCount.OrderByDescending(kv => kv.Value).First().Key;
            target = from.Other();
            if (total == words.Count)
                foreach (var i in neutral) runs[i] = Swap(runs[i], from);
        }

        return new Conversion(string.Concat(runs), target, total, fixedCount, notes);
    }

    Decision Decide(List<Word> group, Mode mode, List<string> notes)
    {
        var script = group[0].Script;
        if (mode == Mode.ForceSwap)
        {
            notes.Add($"{script}: force swap");
            return Decision.Swap;
        }
        // Evidence for "right layout": a known word (1), or a 3+ letter typo with a close spelling (0.75).
        // Evidence for "wrong layout": the swapped word is known (1), only 0.5 for swapped words of 1-3
        // letters, since dictionaries accept many short letter strings.
        double asTyped = 0, afterSwap = 0;
        foreach (var w in group)
        {
            if (w.OrigOk) asTyped += 1;
            else if (w.Core.Length >= 3 && CloseGuess(w.Core, script.Lang()) is not null) asTyped += 0.75;
            if (w.SwapOk) afterSwap += w.SwappedCore.Length <= 3 ? 0.5 : 1;
        }
        var score = $"as typed {asTyped}, after swap {afterSwap}";
        // A sentence typed on the wrong layout: every word reads as a real word after the swap, and
        // something as typed is not a word. Short words alone score low on both sides ("how are you"
        // typed on Hebrew reads as two real Hebrew words and a typo), so the whole sentence decides.
        if (group.Count >= 2 && group.All(w => w.SwapOk) && group.Any(w => !w.OrigOk))
        {
            notes.Add($"{script}: {score}, every word reads after swap -> swap");
            return Decision.Swap;
        }
        if (afterSwap > asTyped)
        {
            notes.Add($"{script}: {score} -> swap");
            return Decision.Swap;
        }
        if (asTyped > 0)
        {
            notes.Add($"{script}: {score} -> fix");
            return Decision.Fix;
        }
        notes.Add($"{script}: {score}, nothing known -> swap");
        return Decision.Swap;
    }

    /// <summary>Best suggestion within a small edit distance, or null. Hebrew final-letter mistakes count as 0.</summary>
    public string? CloseGuess(string word, Lang lang)
    {
        if (lang == Lang.He)
        {
            var f = TextTools.FixHebrewFinals(word);
            if (f != word && _spell.IsCorrect(f, Lang.He)) return f;
        }
        var target = TextTools.Fold(word);
        int maxDistance = word.Length <= 4 ? 1 : 2;
        (int D, string Word)? best = null;
        // A guess that differs only in capital letters ("Vhh" for "vhh") is not a spelling fix (#30).
        var lowered = word.ToLowerInvariant();
        foreach (var g in _spell.Guesses(word, lang))
        {
            if (g.ToLowerInvariant() == lowered) continue;
            int d = TextTools.Distance(target, TextTools.Fold(g));
            if (d <= maxDistance && (best is null || d < best.Value.D)) best = (d, g);
        }
        return best is { } b ? TextTools.MatchCase(b.Word, word) : null;
    }
}
