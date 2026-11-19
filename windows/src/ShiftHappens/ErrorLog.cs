using System.Text;

namespace ShiftHappens;

/// <summary>Errors the app caught, appended to %LOCALAPPDATA%\ShiftHappens\error.log, so a crash or a failed
/// clipboard step can be reported (#27). Never throws.</summary>
static class ErrorLog
{
    const long MaxBytes = 256 * 1024;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShiftHappens", "error.log");

    public static void Write(string where, Exception e) => Write(where, e.ToString());

    public static void Write(string where, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Keep the file small: start over when it gets big.
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
            File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {AppInfo.Version} {where}: {message}{Environment.NewLine}",
                new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }
}
