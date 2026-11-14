using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;
using ShiftCore;

namespace ShiftHappens;

/// <summary>The app's own version and a one-line description of this PC, for the Settings window, the
/// update check and prefilled issue forms.</summary>
static class AppInfo
{
    public static string Version => Application.ProductVersion.Split('+')[0];

    public static string OsDescription
    {
        get
        {
            var v = Environment.OSVersion.Version;
            var name = v.Major == 10 && v.Build >= 22000 ? "Windows 11" : $"Windows {v.Major}";
            string? display = null;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                display = key?.GetValue("DisplayVersion") as string;
            }
            catch (Exception)
            {
                // Not readable: leave the build number only.
            }
            return display is null ? $"{name} (build {v.Build})" : $"{name} {display} (build {v.Build})";
        }
    }

    public static void Open(Uri url) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });

    public static void OpenIssue(Releases.IssueKind kind) => Open(Releases.IssueUrl(kind, Version, OsDescription));
}

/// <summary>Asks GitHub once a day whether a newer release exists. Only the public <c>releases/latest</c>
/// API is called; nothing about the user is sent. Opening the release page is the update: the exe is not
/// signed, so it does not replace itself. Runs on the UI thread (WinForms timer, async continuations).</summary>
sealed class UpdateChecker
{
    static readonly HttpClient Http = CreateClient();
    static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    readonly Settings _settings;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 60 * 60 * 1000 };
    bool _checking;

    public Releases.Latest? Latest { get; private set; }
    public string Status { get; private set; } = "";
    public event Action? Changed;

    /// <summary>A newer release than this app, if the last check found one.</summary>
    public Releases.Latest? Available =>
        Latest is { } l && Releases.IsNewer(l.Version, AppInfo.Version) ? l : null;

    public UpdateChecker(Settings settings)
    {
        _settings = settings;
        _timer.Tick += (_, _) => CheckIfDue();
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ShiftHappens/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Checks shortly after launch and then hourly, each time only if a day has passed.</summary>
    public async void Start()
    {
        _timer.Start();
        await Task.Delay(10_000);
        CheckIfDue();
    }

    public void CheckIfDue()
    {
        if (!_settings.CheckForUpdates) return;
        if (_settings.LastUpdateCheck is { } last && DateTime.UtcNow - last < Interval) return;
        _ = CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        Status = "Checking…";
        Changed?.Invoke();
        try
        {
            using var response = await Http.GetAsync(Releases.LatestApi);
            var found = response.IsSuccessStatusCode
                ? Releases.DecodeLatest(await response.Content.ReadAsStringAsync())
                : null;
            if (found is null)
            {
                Status = "Could not check for updates";
                return;
            }
            Latest = found;
            _settings.LastUpdateCheck = DateTime.UtcNow;
            _settings.Save();
            Status = Available is { } a ? $"Version {a.Version} is available" : "You have the latest version";
        }
        catch (Exception)
        {
            Status = "Could not check for updates (offline?)";
        }
        finally
        {
            _checking = false;
            Changed?.Invoke();
        }
    }
}
