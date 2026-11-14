using System.Windows.Automation;
using System.Windows.Automation.Text;
using ShiftCore;

namespace ShiftHappens;

/// <summary>
/// Selects the word before the cursor through UI Automation: takes the focused control's caret as a
/// text range and moves its start back one character at a time in logical order, with the same
/// <see cref="WordWalk"/> steps as the key walk. Unlike Shift+Left this is right for right-to-left
/// text. Only an Unsupported outcome lets the caller fall back to the key walk.
/// </summary>
static class UiaWord
{
    public sealed record Selection(string Expected, Action Restore);

    public enum Kind
    {
        /// <summary>The word is selected.</summary>
        Selected,
        /// <summary>The control's text was read and there is no word before the caret. Final: do not
        /// fall back to Shift+Left, which in right-to-left text would select the text after the caret.</summary>
        NoWord,
        /// <summary>No TextPattern, or it cannot select: the caller may use Shift+Left.</summary>
        Unsupported,
    }

    public sealed record Outcome(Kind Kind, Selection? Selection = null);

    static readonly Outcome UnsupportedOutcome = new(Kind.Unsupported);

    public static Outcome Select()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || !element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return UnsupportedOutcome;
            var text = (TextPattern)pattern;
            var ranges = text.GetSelection();
            if (ranges.Length != 1) return UnsupportedOutcome;
            var caret = ranges[0];
            if (caret.GetText(1).Length != 0) return UnsupportedOutcome;  // something is selected after all

            var range = caret.Clone();
            var walk = new WordWalk();
            for (int i = 0; i < walk.MaxCharacters * 2 + 4; i++)
            {
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
                var step = walk.Next(range.GetText(-1));
                if (step == WordWalk.Step.Extend) continue;
                if (step == WordWalk.Step.Fail) return new Outcome(Kind.NoWord);
                if (step == WordWalk.Step.DoneShrink)
                    range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, 1);
                range.Select();
                return new Outcome(Kind.Selected, new Selection(range.GetText(-1), () =>
                {
                    try { caret.Select(); } catch (Exception) { /* control went away */ }
                }));
            }
            return new Outcome(Kind.NoWord);
        }
        catch (Exception)
        {
            // ElementNotAvailable, InvalidOperation (read-only or no selection support), COM errors.
            return UnsupportedOutcome;
        }
    }
}
