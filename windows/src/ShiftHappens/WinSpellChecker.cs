using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ShiftCore;

namespace ShiftHappens;

/// <summary>The Windows Spell Checking API (Windows 8+), the same checker Windows apps use.
/// A language whose spell checker is not installed counts every word as unknown: the app still swaps
/// layouts, it just cannot fix typos in that language.</summary>
sealed class WinSpellChecker : ISpellChecking
{
    readonly ISpellChecker? _en;
    readonly ISpellChecker? _he;

    public bool HasEnglish => _en is not null;
    public bool HasHebrew => _he is not null;

    public WinSpellChecker()
    {
        try
        {
            var factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass();
            _en = Create(factory, "en-US");
            _he = Create(factory, "he-IL") ?? Create(factory, "he");
        }
        catch (COMException)
        {
            // No spell checking service on this machine.
        }
    }

    static ISpellChecker? Create(ISpellCheckerFactory f, string tag) =>
        f.IsSupported(tag) ? f.CreateSpellChecker(tag) : null;

    ISpellChecker? For(Lang lang) => lang == Lang.En ? _en : _he;

    public bool IsCorrect(string word, Lang lang)
    {
        var checker = For(lang);
        if (checker is null || word.Length == 0) return false;
        var errors = checker.Check(word);
        try
        {
            return errors.Next(out _) != 0;  // S_FALSE (1): no errors
        }
        finally
        {
            Marshal.ReleaseComObject(errors);
        }
    }

    public IReadOnlyList<string> Guesses(string word, Lang lang)
    {
        var checker = For(lang);
        var result = new List<string>();
        if (checker is null || word.Length == 0) return result;
        var list = checker.Suggest(word);
        try
        {
            var one = new string[1];
            while (list.Next(1, one, IntPtr.Zero) == 0 && result.Count < 20) result.Add(one[0]);
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
        return result;
    }

    // COM declarations from spellcheck.h. Only the members up to the last one used are declared;
    // the vtable order must match the header.

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    class SpellCheckerFactoryClass { }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { get; }

        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError
    {
        [PreserveSig]
        int Next(out ISpellingError? value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError
    {
        uint StartIndex { get; }
        uint Length { get; }
    }
}
