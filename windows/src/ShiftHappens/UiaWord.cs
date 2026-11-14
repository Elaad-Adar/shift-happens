using System.Windows.Automation;
using System.Windows.Automation.Text;
using ShiftCore;

namespace ShiftHappens;

/// <summary>
/// Selects the word before the cursor through UI Automation: takes the focused control's caret as a
/// text range and moves its start back one character at a time in logical order, with the same
/// <see cref="WordWalk"/> steps as the key walk. Unlike Shift+Left this is right for right-to-left
/// text. Null when the control has no TextPattern (or cannot select); the caller then falls back to
/// the key walk.
/// </summary>
static class UiaWord
{
    public sealed record Selection(string Expected, Action Restore);

    public static Selection? Select()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null || !element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
            var text = (TextPattern)pattern;
            var ranges = text.GetSelection();
            if (ranges.Length != 1) return null;
            var caret = ranges[0];
            if (caret.GetText(1).Length != 0) return null;  // something is selected after all

            var range = caret.Clone();
            var walk = new WordWalk();
            for (int i = 0; i < walk.MaxCharacters * 2 + 4; i++)
            {
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1);
                var step = walk.Next(range.GetText(-1));
                if (step == WordWalk.Step.Extend) continue;
                if (step == WordWalk.Step.Fail) return null;
                if (step == WordWalk.Step.DoneShrink)
                    range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, 1);
                range.Select();
                return new Selection(range.GetText(-1), () =>
                {
                    try { caret.Select(); } catch (Exception) { /* control went away */ }
                });
            }
            return null;
        }
        catch (Exception)
        {
            // ElementNotAvailable, InvalidOperation (read-only or no selection support), COM errors.
            return null;
        }
    }
}
