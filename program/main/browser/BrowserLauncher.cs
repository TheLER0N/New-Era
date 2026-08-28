using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace MainApp;

// Раньше запускал внешний Chrome/Edge с плагином. Теперь браузер встроен
// (WebView2-панель), поэтому здесь остались только путь к config и старт gateway.
public static class BrowserLauncher
{
    public static string? GetConfigPath()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "sending and receiving")) || Directory.Exists(Path.Combine(dir, "program")))
            {
                var sr = Path.Combine(dir, "sending and receiving");
                Directory.CreateDirectory(sr);
                return Path.Combine(sr, "config.json");
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(AppContext.BaseDirectory, "config.json");
    }
}

public static class GatewayLauncher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static bool _startRequested;

    public static async Task<bool> EnsureRunningAsync()
    {
        if (await IsRunningAsync()) return true;
        if (!_startRequested)
        {
            _startRequested = true;
            _ = Task.Run(() => { try { GatewayHost.Start(); } catch { } });
        }
        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(500);
            if (await IsRunningAsync()) return true;
        }
        return false;
    }

    private static async Task<bool> IsRunningAsync()
    {
        try
        {
            var text = await Http.GetStringAsync("http://localhost:51234/status");
            return !string.IsNullOrWhiteSpace(text);
        }
        catch { return false; }
    }
}