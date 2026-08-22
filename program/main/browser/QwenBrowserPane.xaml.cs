using System;
using System.IO;
using System.Media;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace MainApp;

public partial class QwenBrowserPane : UserControl
{
    public static QwenBrowserPane Shared { get; } = new();
    private static Window? _host;
    private ClientWebSocket? _ws;
    private bool _wsConnected;
    private readonly DispatcherTimer _wsReconnectTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string _currentChatId = "";
    private int _navRetries;
    private bool _lastThink;
    private bool _webInitialized;
    private readonly SemaphoreSlim _uiLock = new(1, 1);
    private readonly TaskCompletionSource<bool> _readyTcs = new();
    private TaskCompletionSource<string>? _syncTcs;
    private TaskCompletionSource<string>? _sendTcs;
    public Task<bool> ReadyTask => _readyTcs.Task;
    public event Action<string>? BootStatusChanged;
    private readonly DispatcherTimer _bootBarTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
    private int _bootTick;

    public QwenBrowserPane()
    {
        InitializeComponent();
        _wsReconnectTimer.Tick += (_, _) => ConnectWebSocket();
        _wsReconnectTimer.Start();
        _bootBarTimer.Tick += (_, _) =>
        {
            _bootTick++;
            const int cells = 22;
            var head = _bootTick % (cells + 8);
            var ch = new char[cells];
            for (int i = 0; i < cells; i++) ch[i] = '░';
            for (int i = 0; i < 8; i++)
            {
                int idx = head - 4 + i;
                if (idx >= 0 && idx < cells) ch[idx] = (i == 3 || i == 4) ? '█' : '▒';
            }
            BootBar.Text = "[" + new string(ch) + "]";
        };
        _bootBarTimer.Start();

        Loaded += async (_, _) =>
        {
            await InitWebView();
            ConnectWebSocket();
        };
        Unloaded += (_, _) =>
        {
            _wsReconnectTimer.Stop();
            _ws?.Dispose();
            _ws = null;
            _wsConnected = false;
        };
    }

    public event Action? CaptchaDetected;

    public static void EnsureOffscreen()
    {
        if (_host != null) return;
        _host = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -32000,
            Top = 0,
            Width = 1024,
            Height = 768,
            Background = Brushes.Black,
            Content = Shared
        };
        _host.Show();
    }

    public static void ParkOffscreen()
    {
        EnsureOffscreen();
        var pane = Shared;
        if (ReferenceEquals(pane.Parent, _host)) return;
        if (pane.Parent is Panel pp) pp.Children.Remove(pane);
        else if (pane.Parent is Decorator dd) dd.Child = null;
        else if (pane.Parent is ContentControl cc) cc.Content = null;
        _host!.Content = pane;
    }

    public void MountIn(object host)
    {
        if (Parent is Panel p) p.Children.Remove(this);
        else if (Parent is Decorator d) d.Child = null;
        else if (Parent is ContentControl c) c.Content = null;

        if (host is Panel hp)
        {
            hp.Children.Insert(0, this);
            if (hp is Grid g && g.RowDefinitions.Count > 0) Grid.SetRowSpan(this, g.RowDefinitions.Count);
        }
        else if (host is Decorator hd) hd.Child = this;
    }

    private void Boot(string s)
    {
        Dispatcher.InvokeAsync(() => BootText.Text = s);
        BootStatusChanged?.Invoke(s);
    }

    private void MarkReady(bool ok)
    {
        _readyTcs.TrySetResult(ok);
        BootStatusChanged?.Invoke(ok ? "готово" : "ошибка");
        Dispatcher.InvokeAsync(() =>
        {
            _bootBarTimer.Stop();
            if (ok) BootOverlay.Visibility = Visibility.Collapsed;
            else { BootText.Text = "⚠ не загрузилось · нажми ⟳"; BootBar.Text = ""; }
        });
    }

    private async Task InitWebView()
    {
        if (_webInitialized) return;
        _webInitialized = true;
        try
        {
            ConnectionStatus.Text = "Инициализация WebView2...";
            Boot("инициализация WebView2...");
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LERON_CLI", "WebView2Profile"));
            await WebView.EnsureCoreWebView2Async(env);
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                if (e.IsSuccess)
                {
                    _navRetries = 0;
                    StatusText.Text = $"Готово: {WebView.CoreWebView2.Source}";
                    DetectChatId();
                    Boot("синхронизация UI...");
                    await SyncQwenUi();
                    MarkReady(true);
                }
                else if (_navRetries < 2)
                {
                    _navRetries++;
                    StatusText.Text = $"Ошибка {e.WebErrorStatus}, повтор {_navRetries}...";
                    await Task.Delay(1500);
                    try { WebView.CoreWebView2.Navigate(GetStartUrl()); } catch { }
                }
                else
                {
                    StatusText.Text = $"⚠ Не загрузилось: {e.WebErrorStatus}";
                    MarkReady(false);
                }
            };
            WebView.CoreWebView2.WebMessageReceived += OnWebMessage;
            await InjectCrtCss();
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BrowserBridge.BridgeScript);
            var url = GetStartUrl();
            StatusText.Text = $"Загружаю: {url}";
            Boot("загрузка chat.qwen.ai...");
            WebView.CoreWebView2.Navigate(url);
            ConnectionStatus.Text = "WebView2 готов. Загрузка Qwen...";
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = $"Ошибка WebView2: {ex.Message}";
            MarkReady(false);
        }
    }

    private static string GetStartUrl()
    {
        try
        {
            var configPath = BrowserLauncher.GetConfigPath();
            if (configPath != null)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("Roles", out var roles))
                {
                    foreach (var role in roles.EnumerateObject())
                    {
                        if (role.Value.TryGetProperty("Url", out var url) && !string.IsNullOrEmpty(url.GetString()))
                            return url.GetString()!;
                        if (role.Value.TryGetProperty("ChatId", out var cid) && !string.IsNullOrEmpty(cid.GetString()))
                            return $"https://chat.qwen.ai/c/{cid.GetString()}";
                    }
                }
            }
        }
        catch { }
        return "https://chat.qwen.ai/";
    }

    private async Task InjectCrtCss()
    {
        const string css = @"
:root {
  --bg-main: #04150c !important;
  --bg-sidebar: #04150c !important;
  --bg-panel: #04150c !important;
  --text-primary: #c8ffd8 !important;
  --text-secondary: #78b98f !important;
  --accent: #00ff88 !important;
  --border: #123626 !important;
}
html, body, #root, .app, .desktop-layout, .desktop-layout-content, .desktop-layout-content-inner,
.splitter-container, .splitter-container-left-panel, .home-page-layout-main, .main-content,
header, .header-desktop, .header-content, footer,
[class*='sidebar'], [class*='nav'], [class*='layout'], [class*='wrapper'],
[class*='panel'], [class*='chat'], [class*='session'], [class*='dialog'],
[class*='placeholder'], [class*='folder'], [class*='project'], [class*='library'] {
  background-color: var(--bg-main) !important;
  color: var(--text-primary) !important;
}
.sidebar, .sidebar-wrapper, .sidebar-side, .mask {
  background-color: var(--bg-main) !important;
}
[class*='message'], [class*='input'], textarea, [class*='composer'], [class*='editor'],
.message-input, .message-input-wrapper, .message-input-container,
.search-container, .chat-search, [class*='dropdown'], [class*='trigger'], [class*='selector'] {
  background-color: var(--bg-panel) !important;
  color: var(--text-primary) !important;
  border-color: var(--border) !important;
}
[class*='markdown'], [class*='prose'], [class*='message'] *,
[class*='chat-item'] *, [class*='placeholder'] * {
  color: var(--text-primary) !important;
}
a, [class*='link'], .project-item-text, .folder-name,
.chat-item-drag-link-content-tip, .user-menu-btn-text {
  color: var(--text-secondary) !important;
}
button, [class*='btn'], [role='button'] { border-color: var(--border) !important; }
[role='button']:hover, button:hover, .chat-item-drag:hover, .project-item:hover,
.sidebar-entry-list-content:hover {
  background-color: #0f241a !important;
}
::-webkit-scrollbar { width: 8px; }
::-webkit-scrollbar-track { background: #04150c; }
::-webkit-scrollbar-thumb { background: #1d5c3d; border-radius: 4px; }
::selection { background: #123626; color: #c8ffd8; }
";
        var js = "(function(){var css=" + JsonSerializer.Serialize(css) +
                 ";function add(){var t=document.head||document.documentElement;" +
                 "if(!t){setTimeout(add,100);return;}" +
                 "var s=document.createElement('style');s.textContent=css;t.appendChild(s);}add();})();";
        await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(js);
    }

    private void DetectChatId()
    {
        try
        {
            var uri = WebView.CoreWebView2.Source;
            var match = System.Text.RegularExpressions.Regex.Match(uri, @"/c/([a-f0-9-]+)");
            if (match.Success && match.Groups[1].Value != _currentChatId)
            {
                _currentChatId = match.Groups[1].Value;
                ConnectionStatus.Text = $"Чат: {_currentChatId[..8]}...";
                _ws?.SendAsync(
                    Encoding.UTF8.GetBytes($"CHATID:{_currentChatId}"),
                    WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch { }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = e.WebMessageAsJson;
            if (string.IsNullOrEmpty(msg)) return;
            var node = JsonDocument.Parse(msg).RootElement;

            // Строковые сообщения — это результаты скриптов (SYNC:/SENDRES:),
            // т.к. ExecuteScriptAsync не ждёт Promise.
            if (node.ValueKind == JsonValueKind.String)
            {
                var s = node.GetString() ?? "";
                if (s.StartsWith("SYNC:")) _syncTcs?.TrySetResult(s.Substring(5));
                else if (s.StartsWith("SENDRES:")) _sendTcs?.TrySetResult(s.Substring(8));
                return;
            }

            if (node.TryGetProperty("action", out var action))
            {
                var act = action.GetString();
                if (act == "aiResponse" && node.TryGetProperty("text", out var textProp))
                {
                    var text = textProp.GetString() ?? "";
                    var reqid = node.TryGetProperty("reqid", out var rp) ? (rp.GetString() ?? "") : "";
                    if (_wsConnected && _ws != null)
                    {
                        await _ws.SendAsync(
                            Encoding.UTF8.GetBytes($"AI:{_currentChatId}|{reqid}|{text}"),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                        PlayNotificationSound();
                    }
                }
                else if (act == "aiStream" && node.TryGetProperty("text", out var streamText))
                {
                    var text = streamText.GetString() ?? "";
                    Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = $"Стриминг: {text.Length} симв...";
                    });
                }
                // action == "sendfail" больше не обрабатываем: повторная отправка
                // промпта плодила дубли в Qwen. Единственный повтор — при
                // SENDRES:NOT_SENT внутри SendToQwen.
                else if (act == "captcha")
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        CaptchaDetected?.Invoke();
                        StatusText.Text = "⚠ Капча — открой браузер (🌐) и пройди проверку.";
                    });
                }
            }
        }
        catch { }
    }

    private static void PlayNotificationSound()
    {
        try { SystemSounds.Asterisk.Play(); } catch { }
    }

    private async void ConnectWebSocket()
    {
        if (_wsConnected) return;
        try
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri("ws://localhost:51234/ws"), CancellationToken.None);
            _wsConnected = true;
            ConnectionStatus.Text = "Gateway подключён";
            Dispatcher.InvokeAsync(() => LinkDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00ff88")));
            _ = ListenWebSocket();
        }
        catch
        {
            ConnectionStatus.Text = "Gateway недоступен, переподключение...";
            Dispatcher.InvokeAsync(() => LinkDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5a2430")));
        }
    }

    private async Task ListenWebSocket()
    {
        var buffer = new byte[4096];
        try
        {
            while (_ws != null && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (msg.StartsWith("TYPE:"))
                {
                    var parts = msg[5..].Split('|');
                    if (parts.Length >= 5)
                    {
                        var chatId = parts[1];
                        var think = parts[3] == "1";
                        var reqid = parts[4];
                        var text = string.Join('|', parts[5..]);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!string.IsNullOrEmpty(chatId)) _currentChatId = chatId;
                            SendToQwen(text, think, reqid);
                        });
                    }
                }
            }
        }
        catch { }
        finally
        {
            _wsConnected = false;
        }
    }

    private async Task SyncQwenUi()
    {
        try
        {
            await Task.Delay(1200);
            if (WebView?.CoreWebView2 == null) return;
            await _uiLock.WaitAsync();
            try
            {
                var report = await SyncUiNoLockAsync(_lastThink);
                Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = SyncStatusText(report, _lastThink);
                    ConnectionStatus.Text = "Синхронизация: " + SyncStatusText(report, _lastThink);
                });
            }
            finally { _uiLock.Release(); }
        }
        catch { }
    }

    private async Task<string> SyncUiNoLockAsync(bool think)
    {
        if (WebView?.CoreWebView2 == null) return "sync:no-corewebview";
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncTcs = tcs;
        await WebView.CoreWebView2.ExecuteScriptAsync(
            BrowserBridge.SyncUiScript.Replace("__THINK__", think ? "true" : "false"));
        // 15 секунд: открытие меню + переключение модели (перезагрузка чата ~1.5с) + повтор.
        var finished = await Task.WhenAny(tcs.Task, Task.Delay(15000));
        if (finished != tcs.Task) return "sync-timeout";
        return await tcs.Task;
    }

    private async Task<string> WaitSendAsync(string script)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sendTcs = tcs;
        await WebView.CoreWebView2.ExecuteScriptAsync(script);
        var finished = await Task.WhenAny(tcs.Task, Task.Delay(25000));
        return finished == tcs.Task ? await tcs.Task : "send-timeout";
    }

    private static (bool allowed, string reason) ModelGate(string report)
    {
        if (string.IsNullOrWhiteSpace(report)) return (true, "");
        bool sawModel = false;
        string model = "";
        foreach (var raw in report.Split(' '))
        {
            var p = raw.Trim();
            if (!p.StartsWith("model:", StringComparison.OrdinalIgnoreCase)) continue;
            sawModel = true;
            var v = p.Substring(6).Trim().ToLowerInvariant();
            if (v == "ok" || v == "no-ui" || v == "?" || v == "")
                return (true, "");
            if (v == "menu-fail" || v.Contains("menu-fail"))
                return (false, "не удалось открыть/выбрать модель 3.8-Max");
            if (v.Contains("3.8") && v.Contains("max"))
                return (true, "");
            model = v;
        }
        if (!sawModel || string.IsNullOrWhiteSpace(model)) return (true, "");
        return (false, $"модель не 3.8-Max ({model})");
    }

    private static string SyncStatusText(string report, bool think)
    {
        if (string.IsNullOrWhiteSpace(report)) return "⚠ синхронизация: нет ответа";
        bool warn =
            report.Contains("no-ui") ||
            report.Contains("timeout") ||
            report.Contains("sync-err") ||
            report.Contains("menu-fail") ||
            report.StartsWith("sync:");
        string desired = think ? "🧠 мышление" : "⚡ быстро";
        if (report.Contains("model:ok") && report.Contains("think:ok"))
            return $"3.8 max · {(think ? "мышление" : "быстро")}";
        if (warn) return $"⚠ {desired} · {report}";
        return $"{desired} · {report}";
    }

    public async void SetThinkMode(bool think)
    {
        _lastThink = think;
        try
        {
            if (WebView?.CoreWebView2 == null) return;
            Dispatcher.InvokeAsync(() =>
                StatusText.Text = think ? "🧠 переключаю на мышление..." : "⚡ переключаю на быстрый...");
            await _uiLock.WaitAsync();
            try
            {
                var r = await SyncUiNoLockAsync(think);
                Dispatcher.InvokeAsync(() =>
                {
                    if (r.Contains("no-ui") || r.Contains("timeout") || r.Contains("menu-fail"))
                    {
                        StatusText.Text = "⚠ Не нашёл тумблер мышления/модели в Qwen";
                        ConnectionStatus.Text = "Синхронизация: ⚠ " + r;
                    }
                    else
                    {
                        StatusText.Text = (think ? "🧠 Qwen: мышление" : "⚡ Qwen: быстро") + " · " + r;
                        ConnectionStatus.Text = "Синхронизация: " + r;
                    }
                });
            }
            finally { _uiLock.Release(); }
        }
        catch { }
    }

    private async Task SendBlockedResponseAsync(string reqid, string reason)
    {
        try
        {
            if (_ws == null || !_wsConnected || _ws.State != WebSocketState.Open) return;
            var safe = (reason ?? "").Replace('|', '/');
            var text = $"⚠ {safe}";
            var payload = $"AI:{_currentChatId}|{reqid}|{text}";
            await _ws.SendAsync(
                Encoding.UTF8.GetBytes(payload),
                WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    private async void SendToQwen(string text, bool think, string reqid)
    {
        try
        {
            _lastThink = think;
            Dispatcher.InvokeAsync(() =>
                StatusText.Text = think ? "🧠 проверяю 3.8 max и режим..." : "⚡ проверяю 3.8 max и режим...");
            await _uiLock.WaitAsync();
            try
            {
                if (WebView?.CoreWebView2 == null)
                {
                    Dispatcher.InvokeAsync(() => StatusText.Text = "⚠ WebView2 не готов");
                    await SendBlockedResponseAsync(reqid, "WebView2 не готов");
                    return;
                }

                string syncReport;
                try { syncReport = await SyncUiNoLockAsync(think); }
                catch (Exception ex) { syncReport = "sync-error:" + ex.Message; }

                var gate = ModelGate(syncReport);
                if (!gate.allowed)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        StatusText.Text = $"⚠ заблокировано: {gate.reason}";
                        ConnectionStatus.Text = $"Синхронизация: ⚠ {gate.reason} · {syncReport}";
                    });
                    await SendBlockedResponseAsync(reqid, gate.reason);
                    return;
                }

                var syncStatus = SyncStatusText(syncReport, think);
                Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = "Синхронизация: " + syncStatus;
                    ConnectionStatus.Text = "Синхронизация: " + syncStatus;
                });

                var script = BrowserBridge.SendScript
                    .Replace("__TEXT__", JsonSerializer.Serialize(text))
                    .Replace("__REQID__", reqid);

                // Одна отправка. Повтор максимум 1 раз и ТОЛЬКО если скрипт явно вернул NOT_SENT.
                var status = await WaitSendAsync(script);
                if (status == "NOT_SENT")
                {
                    await Task.Delay(2500);
                    status = await WaitSendAsync(script);
                }

                var ok = status == "OK";
                Dispatcher.InvokeAsync(() =>
                    StatusText.Text = ok ? $"Отправлено · {syncStatus}" : $"⚠ Не отправилось ({status}) · {syncStatus}");
            }
            finally { _uiLock.Release(); }
        }
        catch (Exception ex)
        {
            Dispatcher.InvokeAsync(() => StatusText.Text = $"Ошибка: {ex.Message}");
        }
    }

    private void OnBackClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void OnForwardClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoForward == true)
            WebView.CoreWebView2.GoForward();
    }

    private void OnReloadClick(object sender, System.Windows.RoutedEventArgs e)
    {
        WebView.CoreWebView2?.Reload();
    }
}