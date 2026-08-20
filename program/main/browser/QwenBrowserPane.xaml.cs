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
// Единственный экземпляр на всё приложение.
public static QwenBrowserPane Shared { get; } = new();
// Offscreen-окно для прелоада: WebView2 (HwndHost) нельзя накрыть WPF-слоем,
// поэтому пока браузер не нужен — он живёт и грузится в окне за экраном.
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
public Task<bool> ReadyTask => _readyTcs.Task;
public event Action<string>? BootStatusChanged;
private readonly DispatcherTimer _bootBarTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
private int _bootTick;
// Бридж: полный текст берём из SSE-стрима; DOM-захват — только фолбэк.
// __LERON_STREAMING__ блокирует DOM-захват во время генерации, чтобы не резать текст.
private const string BridgeScript = """
(function(){
if (window.__LERON_BRIDGE__) return;
window.__LERON_BRIDGE__ = true;
var lastText = '';
var lastChange = 0;
window.__LERON_STREAMING__ = false;
function assistantEls(){
var candidates = [
'[data-testid="assistant-message"]',
'[data-message-role="assistant"]',
'[class*="message-assistant"]',
'[class*="assistant-message"]',
'[class*="response-message"]',
'.assistant-message',
'[data-role="assistant"]',
'[class*="bot-message"]',
'[class*="ai-message"]',
'[class*="markdown-body"]'
];
for (var i=0;i<candidates.length;i++){
var els = document.querySelectorAll(candidates[i]);
if (els.length > 0) return Array.prototype.slice.call(els);
}
var containers = document.querySelectorAll('[class*="chat-message"], [class*="message-item"]');
var out = [];
for (var j=0;j<containers.length;j++){
var cls = (typeof containers[j].className === 'string' ? containers[j].className : '').toLowerCase();
if (cls.indexOf('user')>=0 || cls.indexOf('human')>=0) continue;
if (containers[j].querySelector('[class*="markdown"], [class*="prose"], [class*="content"]'))
out.push(containers[j]);
}
return out;
}
function extractText(el){
var inner = el.querySelector('[class*="markdown"]') || el.querySelector('[class*="content"]') ||
el.querySelector('[class*="prose"]') || el;
return (inner.innerText || '').trim();
}
function findStopButton(){
var btns = Array.prototype.slice.call(document.querySelectorAll('button'));
for (var i=0;i<btns.length;i++){
var b = btns[i];
var label = (b.getAttribute('aria-label') || '').toLowerCase();
var text = (b.innerText || '').toLowerCase();
if (label.indexOf('stop')>=0 || label.indexOf('cancel')>=0 || label.indexOf('останов')>=0 ||
text.indexOf('stop')>=0 || text.indexOf('остановить')>=0) return b;
}
return null;
}
function reportAi(text){
if (!text) return;
window.__LERON_LAST_AI__ = text;
lastText = text;
lastChange = Date.now();
if (window.chrome && window.chrome.webview)
window.chrome.webview.postMessage({ action: 'aiResponse', text: text });
}
function check(){
if (!window.__LERON_EXPECT__) return;
if (window.__LERON_STREAMING__) return;
if (findStopButton()) return;
var els = assistantEls();
if (els.length === 0) return;
var cur = extractText(els[els.length-1]);
if (cur !== lastText){ lastText = cur; lastChange = Date.now(); }
if ((Date.now() - lastChange) < 3500) return;
if (!cur || cur === window.__LERON_LAST_AI__) return;
window.__LERON_EXPECT__ = false;
reportAi(cur);
}
function startObserve(){
if (!document.body){ setTimeout(startObserve, 100); return; }
var observer = new MutationObserver(check);
observer.observe(document.body, { childList:true, subtree:true, characterData:true });
setInterval(check, 800);
}
startObserve();
var origFetch = window.fetch;
window.fetch = async function(){
var response = await origFetch.apply(this, arguments);
var url = typeof arguments[0] === 'string' ? arguments[0] : (arguments[0] && arguments[0].url || '');
if (response.ok && (url.indexOf('/chat')>=0 || url.indexOf('/completion')>=0)) {
window.__LERON_STREAMING__ = true;
try {
var clone = response.clone();
var reader = clone.body.getReader();
var decoder = new TextDecoder();
var full = '';
var NL = String.fromCharCode(10);
(async function(){
try {
while(true){
var r = await reader.read();
if (r.done){
window.__LERON_STREAMING__ = false;
if (window.__LERON_EXPECT__ && full){
window.__LERON_EXPECT__ = false;
reportAi(full);
}
break;
}
var chunk = decoder.decode(r.value, {stream:true});
var lines = chunk.split(NL);
for (var i=0;i<lines.length;i++){
if (lines[i].indexOf('data: ')===0){
var data = lines[i].substring(6).trim();
if (data==='[DONE]') continue;
try {
var json = JSON.parse(data);
var delta = (json.choices && json.choices[0] && json.choices[0].delta && json.choices[0].delta.content) || '';
if (delta){
full += delta;
if (window.chrome && window.chrome.webview)
window.chrome.webview.postMessage({ action: 'aiStream', text: full, delta: delta });
}
} catch(e){}
}
}
}
} catch(e){ window.__LERON_STREAMING__ = false; }
})();
} catch(e){ window.__LERON_STREAMING__ = false; }
}
return response;
};
setInterval(function(){
var c = document.querySelector('iframe[src*="challenges.cloudflare.com"], #challenge-stage, #px-captcha, [class*="captcha"], [id*="captcha"]');
var t = (document.body ? document.body.innerText : '').slice(0,3000).toLowerCase();
var hit = !!c || t.indexOf('verify you are human')>=0 || t.indexOf('checking your browser')>=0 || t.indexOf('проверьте, что вы человек')>=0;
if (hit && window.chrome && window.chrome.webview)
window.chrome.webview.postMessage({ action: 'captcha' });
}, 3000);
})();
""";
private const string SyncUiScript = @"
(async function() {
var wantThink = __THINK__;
function wait(ms) { return new Promise(function(r) { setTimeout(r, ms); }); }
function vis(el) { return !!el && (el.offsetParent !== null || el.getClientRects().length > 0); }
function txt(el) { return (el.innerText || '').trim(); }
var report = [];
var modelLabel = document.querySelector('.wms-trigger__text');
if (modelLabel) {
var cm = txt(modelLabel).toLowerCase();
if (cm.indexOf('3.8') < 0 || cm.indexOf('max') < 0) {
var mt = document.querySelector('.wms-trigger');
if (mt) {
mt.click();
await wait(300);
var mEls = document.querySelectorAll('div, span, li');
var mBest = null; var mLen = 1e9;
for (var i = 0; i < mEls.length; i++) {
var el = mEls[i];
if (!vis(el)) continue;
var t = txt(el).toLowerCase();
if (!t || t.length > 40) continue;
if (t.indexOf('3.8') >= 0 && t.indexOf('max') >= 0 && t.length < mLen) { mBest = el; mLen = t.length; }
}
if (mBest) { mBest.click(); await wait(200); } else { mt.click(); await wait(100); }
report.push('model:' + txt(document.querySelector('.wms-trigger__text') || modelLabel));
}
} else report.push('model:ok');
} else report.push('model:no-ui');
var tLabel = document.querySelector('.qwen-thinking-selector .qwen-chat-v2-dropdown-menu-select-label');
if (tLabel) {
var cur = txt(tLabel).toLowerCase();
var isThink = cur.indexOf('мышл') >= 0 || cur.indexOf('think') >= 0 || cur.indexOf('reason') >= 0;
if (isThink !== wantThink) {
var sel = document.querySelector('.qwen-thinking-selector .qwen-chat-v2-dropdown-menu-select');
if (sel) {
sel.click();
await wait(250);
var words = wantThink ? ['мышление', 'мышл', 'thinking', 'think'] : ['быстрый', 'быстр', 'fast'];
var els = document.querySelectorAll('div, span, li');
var best = null; var bLen = 1e9;
for (var k = 0; k < els.length; k++) {
var el2 = els[k];
if (!vis(el2) || el2 === tLabel) continue;
var t2 = txt(el2).toLowerCase();
if (!t2 || t2.length > 20) continue;
var hit = false;
for (var w = 0; w < words.length; w++) { if (t2.indexOf(words[w]) >= 0) { hit = true; break; } }
if (hit && t2.length < bLen) { best = el2; bLen = t2.length; }
}
if (best) { best.click(); await wait(150); } else { sel.click(); await wait(100); }
var nl = document.querySelector('.qwen-thinking-selector .qwen-chat-v2-dropdown-menu-select-label');
report.push('think:' + txt(nl || tLabel));
}
} else report.push('think:ok');
} else report.push('think:no-ui');
return report.join(' ');
})();";
private const string SendScript = @"
(async function() {
window.__LERON_EXPECT__ = true;
var text = __TEXT__;
function wait(ms) { return new Promise(function(r) { setTimeout(r, ms); }); }
var input = document.querySelector('textarea') || document.querySelector('[contenteditable=""true""]');
if (!input) return 'NO_INPUT';
input.focus();
var isTa = input.tagName === 'TEXTAREA' || input.tagName === 'INPUT';
function put(chunk) {
if (isTa) {
var s = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
s.call(input, input.value + chunk);
input.dispatchEvent(new Event('input', { bubbles: true }));
} else {
document.execCommand('insertText', false, chunk);
}
}
function clearInput() {
if (isTa) {
var s2 = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
s2.call(input, '');
input.dispatchEvent(new Event('input', { bubbles: true }));
} else {
document.execCommand('selectAll', false, null);
document.execCommand('delete', false, null);
}
}
function send() {
var btns2 = Array.prototype.slice.call(document.querySelectorAll('button'));
var sb = null;
for (var j = 0; j < btns2.length; j++) {
var b2 = btns2[j];
if (!b2.disabled && (b2.getAttribute('aria-label') || '').toLowerCase().indexOf('send') >= 0) { sb = b2; break; }
}
if (sb) sb.click();
else input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
}
clearInput();
if (text.length > 200) {
put(text);
await wait(300);
send();
} else {
await new Promise(function(resolve) {
var i = 0;
(function step() {
if (i >= text.length) { setTimeout(function() { send(); resolve(); }, 200 + Math.random() * 300); return; }
var take = 1 + Math.floor(Math.random() * 3);
put(text.substring(i, i + take));
i += take;
setTimeout(step, 20 + Math.random() * 60);
})();
});
}
return 'OK';
})();";
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
// Старт прелоада: панель грузит Qwen в окне за экраном.
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
// Вернуть панель в offscreen-окно (браузер остаётся живым и загруженным).
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
// Перенос единственной панели между окнами без пересоздания WebView2.
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
await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
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
// Единый тон фона во всём встроенном чате: ВСЕ фоновые переменные = #04150c,
// элементы различаются только бордерами (#123626). Селекторы — по реальному DOM Qwen.
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
if (node.TryGetProperty("action", out var action))
{
var act = action.GetString();
if (act == "aiResponse" && node.TryGetProperty("text", out var textProp))
{
var text = textProp.GetString() ?? "";
if (_wsConnected && _ws != null)
{
await _ws.SendAsync(
Encoding.UTF8.GetBytes($"AI:{_currentChatId}|{text}"),
WebSocketMessageType.Text, true, CancellationToken.None);
PlayNotificationSound();
}
}
if (act == "aiStream" && node.TryGetProperty("text", out var streamText))
{
var text = streamText.GetString() ?? "";
Dispatcher.InvokeAsync(() =>
{
StatusText.Text = $"Стриминг: {text.Length} симв...";
});
}
if (act == "captcha")
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
if (parts.Length >= 4)
{
var text = string.Join('|', parts[4..]);
var think = parts[3] == "1";
await Dispatcher.InvokeAsync(() => SendToQwen(text, think));
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
await WebView.CoreWebView2.ExecuteScriptAsync(
SyncUiScript.Replace("__THINK__", _lastThink ? "true" : "false"));
}
finally { _uiLock.Release(); }
}
catch { }
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
var result = await WebView.CoreWebView2.ExecuteScriptAsync(
SyncUiScript.Replace("__THINK__", think ? "true" : "false"));
var r = result?.Trim('"');
Dispatcher.InvokeAsync(() =>
{
if (r != null && r.Contains("no-ui"))
StatusText.Text = "⚠ Не нашёл тумблер мышления/модели в Qwen";
else
StatusText.Text = (think ? "🧠 Qwen: мышление" : "⚡ Qwen: быстро") + " · " + r;
});
}
finally { _uiLock.Release(); }
}
catch { }
}
private async void SendToQwen(string text, bool think)
{
try
{
_lastThink = think;
Dispatcher.InvokeAsync(() => StatusText.Text = "⏳ режим + отправка...");
await _uiLock.WaitAsync();
try
{
await WebView.CoreWebView2.ExecuteScriptAsync(
SyncUiScript.Replace("__THINK__", think ? "true" : "false"));
var script = SendScript.Replace("__TEXT__", JsonSerializer.Serialize(text));
var result = await WebView.CoreWebView2.ExecuteScriptAsync(script);
Dispatcher.InvokeAsync(() =>
StatusText.Text = result?.Contains("OK") == true ? "Отправлено" : "Ошибка ввода");
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