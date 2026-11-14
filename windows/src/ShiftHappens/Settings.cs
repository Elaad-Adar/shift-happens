using System.Text.Json;
using Microsoft.Win32;

namespace ShiftHappens;

/// <summary>A hotkey as RegisterHotKey wants it: MOD_* flags and a virtual-key code.</summary>
sealed record Shortcut(uint Modifiers, uint Key)
{
    public static readonly Shortcut DefaultConvert = new(Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.Space);

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((Modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((Modifiers & Native.MOD_SHIFT) != 0) parts.Add("Shift");
            if ((Modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName((Keys)Key));
            return string.Join("+", parts);
        }
    }

    static string KeyName(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (k - Keys.D0))).ToString(),
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.Oemtilde => "`",
        Keys.OemPipe => "\\",
        _ => k.ToString(),
    };
}

/// <summary>User settings in %APPDATA%\ShiftHappens\settings.json.</summary>
sealed class Settings
{
    public Shortcut? ConvertShortcut { get; set; } = Shortcut.DefaultConvert;
    public Shortcut? ForceSwapShortcut { get; set; }
    public bool SwitchLayoutAfterSwap { get; set; } = true;
    public bool WelcomeShown { get; set; }

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShiftHappens");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception)
        {
            // Unreadable settings: start from defaults rather than fail to launch.
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}

/// <summary>"Start with Windows": a value under HKCU\...\Run pointing at this exe.</summary>
static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "ShiftHappens";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, Command);
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
