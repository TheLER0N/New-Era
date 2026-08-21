using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace MainApp;
public partial class MainWindow : ChromeWindow
{
private readonly HttpClient _http = new()
{
Timeout = System.Threading.Timeout.InfiniteTimeSpan
};
private string? _selectedRole = "coder";
private readonly HashSet<string> _boundRoles = new();
private Dictionary<string, List<ChatMessage>> _history = new();
private bool _waiting;
private readonly DispatcherTimer _waitTimer = new() { Interval = TimeSpan.FromSeconds(1) };
private DateTime _waitStart;
private readonly string? _projectPath;
private readonly string? _projectName;
private string _mode;
private bool _think;
private readonly QwenBrowserPane _browserPane = QwenBrowserPane.Shared;
private static readonly FontFamily Mono = new("Consolas");
private string HistoryKey => _projectPath != null
? HistoryStore.ProjectKey(_projectPath)
: (_selectedRole ?? "unknown");
public MainWindow(string? projectPath = null, string? projectName = null)
{
InitializeComponent();
_browserPane.MountIn(BrowserPaneHost);
_browserPane.CaptchaDetected += () =>
{
if (BrowserColumn.Width.Value < 1) OnToggleBrowserClick(this, new RoutedEventArgs());
RoleStatus.Text = "⚠ Капча — пройди проверку в окне браузера.";
};
_projectPath = projectPath;
_projectName = projectName;
_mode = projectPath != null ? "edit" : "chat";
LoadHistoryFromDisk();
InputBox.TextChanged += (_, _) =>
InputPlaceholder.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
KeyDown += OnMainWindowKeyDown;
_waitTimer.Tick += (_, _) =>
{
var sec = (int)(DateTime.Now - _waitStart).TotalSeconds;
if (_selectedRole != null)
RoleStatus.Text = $"Ожидание ответа от {_selectedRole}... {sec}с (кнопка «Отправить» стала «стоп»)";
};
var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
statusTimer.Tick += (_, _) => LoadRolesStatus();
statusTimer.Start();
LoadRolesStatus();
RenderHistory();
UpdateModeButtons();
SessionProjectText.Text = _projectName ?? "—";
}
private static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
private void OnToggleBrowserClick(object sender, RoutedEventArgs e)
{
bool show = BrowserColumn.Width.Value < 1;
BrowserColumn.Width = show ? new GridLength(500) : new GridLength(0);
ToggleBrowserBtn.Content = show ? "🌐 браузер: показан" : "🌐 браузер: скрыт";
}
private void OnModeAutoClick(object sender, RoutedEventArgs e) => SetMode("auto");
private void OnModeYoloClick(object sender, RoutedEventArgs e) => SetMode("yolo");
private void OnThinkClick(object sender, RoutedEventArgs e) => ToggleThink();
private void ToggleThink()
{
_think = !_think;
ThinkBtn.Content = _think ? "🧠 мышление" : "⚡ быстро";
UpdateModeButtons();
ApplyRoleSelection();
_browserPane.SetThinkMode(_think);
}
private async void LoadRolesStatus()
{
try
{
var resp = await _http.GetStringAsync("http://localhost:51234/status");
var status = JsonSerializer.Deserialize<GatewayStatus>(
resp,
new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (status == null) return;
_boundRoles.Clear();
if (status.Roles != null)
foreach (var r in status.Roles)
_boundRoles.Add(r);
Dispatcher.InvokeAsync(() =>
{
OnlineDot.Fill = B("#00ff88");
OnlineText.Text = "онлайн";
AgentStatusText.Text = "онлайн · готов к работе";
});
if (_selectedRole != null && !_waiting)
ApplyRoleSelection();
}
catch
{
Dispatcher.InvokeAsync(() =>
{
OnlineDot.Fill = B("#e94560");
OnlineText.Text = "офлайн";
AgentStatusText.Text = "офлайн · gateway недоступен";
RoleStatus.Text = "⚠️ Gateway не запущен";
});
}
}
private void ApplyRoleSelection()
{
if (_selectedRole == null) return;
var bound = _boundRoles.Contains(_selectedRole);
InputBox.IsEnabled = bound;
SendBtn.IsEnabled = bound;
if (!bound)
{
RoleStatus.Text = $"Роль \"{_selectedRole}\" не закреплена за чатом. Открой чат Qwen и закрепи роль через popup плагина.";
return;
}
var proj = _projectPath != null
? $" · 📁 {_projectName ?? _projectPath}"
: "";
RoleStatus.Text = $"Роль: {_selectedRole} · {ModeLabel()} · {(_think ? "🧠 мышление" : "⚡ быстро")}{proj}";
SessionRoleText.Text = _selectedRole;
var label = ModeLabel();
var sp = label.IndexOf(' ');
SessionStyleText.Text = sp >= 0 ? label.Substring(sp + 1) : label;
SessionSpeedText.Text = _think ? "мышление" : "быстро";
SessionProjectText.Text = _projectName ?? "—";
}
private string ModeLabel() => _mode switch
{
"plan" => "📋 планирование",
"edit" => "✏️ аккуратный",
"auto" => "🔄 авто",
"yolo" => "⚡ агрессивный",
_ => "💬 чат"
};
private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
{
if (Keyboard.Modifiers != ModifierKeys.Control) return;
switch (e.Key)
{
case Key.D1:
case Key.NumPad1:
SetMode("chat");
e.Handled = true;
break;
case Key.D2:
case Key.NumPad2:
SetMode("plan");
e.Handled = true;
break;
case Key.D3:
case Key.NumPad3:
SetMode("edit");
e.Handled = true;
break;
case Key.D4:
case Key.NumPad4:
SetMode("auto");
e.Handled = true;
break;
case Key.D5:
case Key.NumPad5:
SetMode("yolo");
e.Handled = true;
break;
case Key.D6:
case Key.NumPad6:
ToggleThink();
e.Handled = true;
break;
}
}
private void OnModeEditClick(object sender, RoutedEventArgs e) => SetMode("edit");
private void OnModeChatClick(object sender, RoutedEventArgs e) => SetMode("chat");
private void OnModeProjectClick(object sender, RoutedEventArgs e) => SetMode("plan");
private void SetMode(string mode)
{
if (_projectPath == null && mode != "chat")
{
RoleStatus.Text = "Режимы проекта доступны только при открытой папке проекта. Сейчас: 💬 чат.";
_mode = "chat";
UpdateModeButtons();
return;
}
_mode = mode;
ApplyRoleSelection();
UpdateModeButtons();
}
private void UpdateModeButtons()
{
StyleModeButton(ModeChatBtn, _mode == "chat");
StyleModeButton(ModeProjectBtn, _mode == "plan");
StyleModeButton(ModeEditBtn, _mode == "edit");
StyleModeButton(ModeAutoBtn, _mode == "auto");
StyleModeButton(ModeYoloBtn, _mode == "yolo");
StyleModeButton(ThinkBtn, _think);
}
private static void StyleModeButton(Button btn, bool active)
{
btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#123626" : "#0b1d14"));
btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00ff88" : "#1d5c3d"));
btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00ff88" : "#c8ffd8"));
}
private void OnToolListClick(object sender, RoutedEventArgs e)
{
InputBox.Text = "/list";
InputBox.Focus();
}
private void OnToolReadClick(object sender, RoutedEventArgs e)
{
InputBox.Text = "/read ./";
InputBox.Focus();
InputBox.SelectionStart = InputBox.Text.Length;
}
private void OnToolWriteClick(object sender, RoutedEventArgs e)
{
InputBox.Text = "/write ./ ";
InputBox.Focus();
InputBox.SelectionStart = InputBox.Text.Length;
}
private void OnToolDeleteClick(object sender, RoutedEventArgs e)
{
InputBox.Text = "/delete ./";
InputBox.Focus();
InputBox.SelectionStart = InputBox.Text.Length;
}
private string ApplyModePrefix(string text)
{
if (_mode == "chat" || string.IsNullOrEmpty(_projectPath))
return text;
var sb = new StringBuilder();
if (_mode is "edit" or "auto" or "yolo")
sb.Append($"[Режим: редактирование. Корень проекта: {_projectPath}. Можно читать и изменять файлы внутри этой папки. Отвечай на русском.] ");
else
sb.Append($"[Режим: обсуждение проекта. Корень проекта: {_projectPath}. НЕ изменяй файлы — только отвечай и объясняй. Отвечай на русском.] ");
sb.Append(text);
return sb.ToString();
}
private void RenderHistory()
{
ChatMessages.Children.Clear();
if (_history.TryGetValue(HistoryKey, out var list))
{
foreach (var m in list)
AddMessageBlock(m.Author, m.Text, m.Bg);
}
}
private void AddMessage(string role, string author, string text, string bgColor)
{
var key = HistoryKey;
if (!_history.ContainsKey(key))
_history[key] = new();
_history[key].Add(new ChatMessage
{
Author = author,
Text = text,
Bg = bgColor
});
SaveHistoryToDisk();
AddMessageBlock(author, text, bgColor);
}
private void AddMessageBlock(string author, string text, string bg)
{
    bool isUser = author.Trim().Equals("ТЫ", StringComparison.OrdinalIgnoreCase);

    var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    // время сверху над пузырем
    var time = new TextBlock
    {
        Text = DateTime.Now.ToString("HH:mm"),
        FontFamily = Theme.Font(),
        FontSize = 12,
        Foreground = Theme.B("#447a5a"),
        Margin = new Thickness(4, 0, 0, 4)
    };
    Grid.SetRow(time, 0);
    Grid.SetColumn(time, 1);
    row.Children.Add(time);

    // аватар прижат к НИЗУ пузыря — как в Telegram
    var avatar = new Border
    {
        Width = 40,
        Height = 40,
        CornerRadius = new CornerRadius(20),
        Background = Theme.B("#0d2418"),
        BorderBrush = isUser ? Theme.B("#00ff88") : Theme.B("#1d5c3d"),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Center
    };
    if (isUser)
    {
        avatar.Child = new System.Windows.Shapes.Path
        {
            Fill = Theme.B("#00ff88"),
            Width = 16,
            Height = 16,
            Stretch = System.Windows.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Data = System.Windows.Media.Geometry.Parse(
                "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z")
        };
    }
    else
    {
        avatar.Child = new TextBlock
        {
            Text = ">_",
            FontFamily = Theme.Font(),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Theme.B("#7dffa8"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
    Grid.SetRow(avatar, 1);
    Grid.SetColumn(avatar, 0);
    row.Children.Add(avatar);

    var bubble = new Border
    {
        Background = Theme.B("#0a1a12"),
        BorderBrush = isUser ? Theme.B("#00ff88") : Theme.B(bg),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 8, 12, 10),
        Margin = new Thickness(4, 0, 0, 0)
    };
    var inner = new StackPanel();
    inner.Children.Add(new TextBlock
    {
        Text = author,
        FontFamily = Theme.Font(),
        FontSize = 11,
        Foreground = isUser ? Theme.B("#00ff88") : Theme.B("#447a5a"),
        Margin = new Thickness(0, 0, 0, 4)
    });
    inner.Children.Add(new TextBlock
    {
        Text = text,
        FontFamily = Theme.Font(),
        FontSize = 14,
        Foreground = Theme.B("#d9ffe7"),
        TextWrapping = TextWrapping.Wrap
    });
    bubble.Child = inner;
    Grid.SetRow(bubble, 1);
    Grid.SetColumn(bubble, 1);
    row.Children.Add(bubble);

    ChatMessages.Children.Add(row);
    ChatScroll.ScrollToEnd();
}
private void OnInputKeyDown(object sender, KeyEventArgs e)
{
if (e.Key == Key.Enter && !_waiting) SendMessage();
}
private void OnSendClick(object sender, RoutedEventArgs e)
{
if (_waiting)
{
_ = CancelWait();
return;
}
SendMessage();
}
private async Task CancelWait()
{
try
{
var payload = JsonSerializer.Serialize(new { role = _selectedRole });
await _http.PostAsync(
"http://localhost:51234/cancel",
new StringContent(payload, Encoding.UTF8, "application/json"));
}
catch { }
}
private void OnBackClick(object sender, RoutedEventArgs e)
{
var hub = new ProjectHubWindow();
hub.StartFullscreen = false;
hub.WindowStartupLocation = WindowStartupLocation.Manual;
hub.Left = Left;
hub.Top = Top;
hub.Width = Width;
hub.Height = Height;
SwapTo(hub);
}
private async void SendMessage()
{
if (_waiting) return;
var text = InputBox.Text.Trim();
if (string.IsNullOrEmpty(text) || _selectedRole == null) return;
var role = _selectedRole;
AddMessage(role, "Ты", text, "#0f3460");
InputBox.Clear();
if (text.StartsWith("/"))
{
RunLocalTool(text);
return;
}
SendBtn.IsEnabled = true;
SendBtn.Content = "⏹ стоп";
_waiting = true;
_waitStart = DateTime.Now;
_waitTimer.Start();
try
{
var agentPayload = JsonSerializer.Serialize(new
{
role,
text,
projectPath = _projectPath,
mode = _mode,
think = _think
});
var agentResp = await _http.PostAsync(
"http://localhost:51234/agent-run",
new StringContent(agentPayload, Encoding.UTF8, "application/json"));
var agentBody = await agentResp.Content.ReadAsStringAsync();
if (agentResp.IsSuccessStatusCode)
{
await HandleAgentBody(role, agentBody);
}
else
{
if (_mode != "chat")
AddMessage(role, "система", "⚠ " + ShortError(agentBody) + " Отвечаю через браузер Qwen.", "#123020");
await SendViaBrowser(role, ApplyModePrefix(text));
}
}
catch (Exception ex)
{
AddMessage(role, "Ошибка", ex.Message, "#e94560");
}
finally
{
_waiting = false;
_waitTimer.Stop();
SendBtn.Content = "Отправить ➤";
SendBtn.IsEnabled = true;
ApplyRoleSelection();
}
}
private static string ShortError(string body)
{
try
{
using var doc = JsonDocument.Parse(body);
if (doc.RootElement.TryGetProperty("error", out var e))
{
var s = e.GetString();
if (!string.IsNullOrEmpty(s))
return s.Length > 220 ? s.Substring(0, 220) + "…" : s;
}
}
catch { }
return body.Length > 220 ? body.Substring(0, 220) + "…" : body;
}
private async Task HandleAgentBody(string role, string body)
{
var r = DeserializeAgent(body);
int guard = 0;
while (r != null && r.Status == "approval" && guard++ < 20)
{
var args = r.Arguments ?? "";
if (args.Length > 700) args = args.Substring(0, 700) + "…";
var yes = MessageBox.Show(
$"Агент хочет выполнить: {r.Tool}\n{args}\nРазрешить?",
"LERON CLI · подтверждение",
MessageBoxButton.YesNo,
MessageBoxImage.Question) == MessageBoxResult.Yes;
AddMessage(role, "подтверждение", $"{(yes ? "✅" : "🚫")} {r.Tool}", "#123020");
var p = JsonSerializer.Serialize(new
{
sessionId = r.SessionId,
approve = yes,
remember = _mode == "auto"
});
var resp = await _http.PostAsync(
"http://localhost:51234/agent-approve",
new StringContent(p, Encoding.UTF8, "application/json"));
body = await resp.Content.ReadAsStringAsync();
if (!resp.IsSuccessStatusCode)
{
AddMessage(role, "Ошибка", body, "#e94560");
return;
}
r = DeserializeAgent(body);
}
if (r == null)
{
AddMessage(role, "Ошибка", "Не удалось разобрать ответ агента.", "#e94560");
return;
}
if (r.Tools != null && r.Tools.Count > 0)
AddMessage(role, "инструменты", string.Join("\n", r.Tools), "#123020");
AddMessage(role, role, string.IsNullOrWhiteSpace(r.Response) ? "(нет ответа)" : r.Response, "#16213e");
}
private static AgentRunResponse? DeserializeAgent(string body)
{
try
{
return JsonSerializer.Deserialize<AgentRunResponse>(
body,
new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
catch
{
return null;
}
}
private async Task SendViaBrowser(string role, string text)
{
try
{
var payload = JsonSerializer.Serialize(new { role, text, think = _think });
var content = new StringContent(payload, Encoding.UTF8, "application/json");
var resp = await _http.PostAsync("http://localhost:51234/send-and-wait", content);
var body = await resp.Content.ReadAsStringAsync();
if (resp.IsSuccessStatusCode)
{
var result = JsonSerializer.Deserialize<SendResponse>(
body,
new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (result != null)
AddMessage(role, role, result.Response, "#16213e");
}
else
{
AddMessage(role, "Ошибка", body, "#e94560");
}
}
catch (Exception ex)
{
AddMessage(role, "Ошибка", ex.Message, "#e94560");
}
}
private void RunLocalTool(string text)
{
var role = _selectedRole!;
if (_projectPath == null)
{
AddMessage(role, "система", "Инструменты доступны только при открытом проекте. Выбери проект в хабе.", "#e94560");
return;
}
var parts = text.Split(new[] { ' ' }, 2);
var cmd = parts[0].ToLowerInvariant();
var rest = parts.Length > 1 ? parts[1].Trim() : "";
string result;
try
{
result = cmd switch
{
"/list" => ToolList(rest),
"/read" => ToolRead(rest),
"/write" => ToolWrite(rest),
"/delete" => ToolDelete(rest),
_ => $"Неизвестная команда: {cmd}. Доступны: /list /read /write /delete"
};
}
catch (Exception ex)
{
result = "Ошибка: " + ex.Message;
}
AddMessage(role, "инструмент", result, "#123020");
}
private string? ResolveInProject(string raw)
{
var trimmed = raw.Trim().Trim('"');
if (string.IsNullOrWhiteSpace(trimmed) || _projectPath == null) return null;
trimmed = trimmed.Replace('/', Path.DirectorySeparatorChar);
var rootFull = Path.GetFullPath(_projectPath);
string full = Path.IsPathRooted(trimmed)
? Path.GetFullPath(trimmed)
: Path.GetFullPath(Path.Combine(rootFull, trimmed));
if (!full.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
!full.StartsWith(rootFull + Path.DirectorySeparatorChar))
return null;
return full;
}
private string ToolList(string rawPath)
{
var p = ResolveInProject(string.IsNullOrWhiteSpace(rawPath) ? "." : rawPath);
if (p == null) return "Доступ отклонён: путь вне проекта.";
if (!Directory.Exists(p)) return $"Папка не найдена: {rawPath}";
var sb = new StringBuilder();
foreach (var d in Directory.GetDirectories(p).OrderBy(x => x))
sb.AppendLine("📁 " + Path.GetFileName(d));
foreach (var f in Directory.GetFiles(p).OrderBy(x => x))
sb.AppendLine("📄 " + Path.GetFileName(f));
var list = sb.ToString();
return string.IsNullOrWhiteSpace(list) ? "(пусто)" : list;
}
private string ToolRead(string rawPath)
{
if (string.IsNullOrWhiteSpace(rawPath)) return "Укажи путь. Пример: /read ./Program.cs";
var p = ResolveInProject(rawPath);
if (p == null) return "Доступ отклонён: путь вне проекта.";
if (!File.Exists(p)) return $"Файл не найден: {rawPath}";
var text = File.ReadAllText(p);
if (text.Length > 20000) text = text.Substring(0, 20000) + "\n…[обрезано]";
return $"--- {rawPath} ---\n{text}";
}
private string ToolWrite(string rest)
{
var wp = rest.Split(new[] { ' ' }, 2);
if (string.IsNullOrWhiteSpace(wp[0])) return "Укажи путь и текст. Пример: /write ./test.txt hello";
var p = ResolveInProject(wp[0]);
if (p == null) return "Доступ отклонён: путь вне проекта.";
var dir = Path.GetDirectoryName(p);
if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
File.WriteAllText(p, wp.Length > 1 ? wp[1] : "");
return $"Файл создан: {wp[0]}";
}
private string ToolDelete(string rawPath)
{
var p = ResolveInProject(rawPath);
if (p == null) return "Доступ отклонён: путь вне проекта.";
if (File.Exists(p)) { File.Delete(p); return $"Файл удалён: {rawPath}"; }
if (Directory.Exists(p)) { Directory.Delete(p, true); return $"Папка удалена: {rawPath}"; }
return $"Не найдено: {rawPath}";
}
private void LoadHistoryFromDisk()
{
_history = HistoryStore.Load();
}
private void SaveHistoryToDisk()
{
HistoryStore.Save(_history);
}
}
public class ChatMessage
{
public string Author { get; set; } = "";
public string Text { get; set; } = "";
public string Bg { get; set; } = "";
}
class GatewayStatus
{
public string Status { get; set; } = "";
public int RolesWithChats { get; set; }
public string[] Roles { get; set; } = [];
}
class SendResponse
{
public string Role { get; set; } = "";
public string Response { get; set; } = "";
}
class AgentRunResponse
{
public string Status { get; set; } = "";
public string SessionId { get; set; } = "";
public string Role { get; set; } = "";
public string Response { get; set; } = "";
public List<string> Tools { get; set; } = new();
public string Tool { get; set; } = "";
public string Arguments { get; set; } = "";
}
public static class HistoryStore
{
public static string ProjectKey(string projectPath) =>
"proj|" + projectPath.TrimEnd('\\', '/').ToLowerInvariant();
public static string? GetPath()
{
var configPath = BrowserLauncher.GetConfigPath();
if (configPath == null) return null;
var dir = Path.GetDirectoryName(configPath);
if (dir == null) return null;
return Path.Combine(dir, "history.json");
}
public static Dictionary<string, List<ChatMessage>> Load()
{
try
{
var path = GetPath();
if (path == null || !File.Exists(path)) return new();
var loaded = JsonSerializer.Deserialize<Dictionary<string, List<ChatMessage>>>(
File.ReadAllText(path),
new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
return loaded ?? new();
}
catch
{
return new();
}
}
public static void Save(Dictionary<string, List<ChatMessage>> map)
{
try
{
var path = GetPath();
if (path == null) return;
File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
}
catch
{
}
}
public static void DeleteProjectHistory(string projectPath)
{
var map = Load();
if (map.Remove(ProjectKey(projectPath)))
Save(map);
}
}