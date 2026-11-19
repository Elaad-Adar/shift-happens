using System.Text;
using ShiftCore;

namespace ShiftHappens;

static class Program
{
    // Command-line modes (run from PowerShell; output goes to that console):
    //   ShiftHappens.exe --dump-layout [file]     layout table as JSON (UTF-8 file, or printed)
    //   ShiftHappens.exe --convert "text" [--force] [--verbose]
    //   ShiftHappens.exe --spell word en|he      one spell check with suggestions
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0) return RunCommandLine(args);

        using var single = new Mutex(true, @"Local\ShiftHappens", out bool first);
        if (!first)
        {
            MessageBox.Show("Shift Happens is already running (see the tray, near the clock).", "Shift Happens");
            return 0;
        }

        // Log errors that escape a handler instead of closing without a trace (#27). UI-thread errors are
        // logged and the app keeps running; others are logged before the process ends.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorLog.Write("ui thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorLog.Write("unhandled", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write("task", e.Exception); e.SetObserved(); };

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
        return 0;
    }

    static int RunCommandLine(string[] args)
    {
        Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine();
        try
        {
            if (args[0] == "--dump-layout")
            {
                var json = WinLayouts.Table(WinLayouts.FindPair()).ToJson();
                // A file path avoids PowerShell 5.1's ">" (UTF-16, and it does not wait for a GUI exe).
                if (args.Length > 1)
                {
                    File.WriteAllText(args[1], json + "\n", new UTF8Encoding(false));
                    Console.WriteLine("wrote " + Path.GetFullPath(args[1]));
                }
                else
                {
                    Console.WriteLine(json);
                }
                return 0;
            }
            if (args[0] == "--convert" && args.Length > 1)
            {
                var converter = new Converter(WinLayouts.Table(WinLayouts.FindPair()), new WinSpellChecker());
                var result = converter.Convert(args[1], args.Contains("--force") ? Mode.ForceSwap : Mode.Smart);
                Console.WriteLine(result.Text);
                if (args.Contains("--verbose"))
                {
                    foreach (var note in result.Notes) Console.WriteLine("  " + note);
                    Console.WriteLine("  switch to: " + (result.SwitchTo?.ToString() ?? "-"));
                }
                return 0;
            }
            if (args[0] == "--spell" && args.Length > 2)
            {
                var spell = new WinSpellChecker();
                var lang = args[2] == "he" ? Lang.He : Lang.En;
                Console.WriteLine($"{args[1]} {(spell.IsCorrect(args[1], lang) ? "OK" : "MISS")} " +
                                  string.Join(", ", spell.Guesses(args[1], lang).Take(5)));
                Console.WriteLine($"  spell checkers: en {(spell.HasEnglish ? "yes" : "no")}, he {(spell.HasHebrew ? "yes" : "no")}");
                return 0;
            }
            Console.Error.WriteLine("usage: --dump-layout [file] | --convert \"text\" [--force] [--verbose] | --spell word en|he");
            return 2;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
