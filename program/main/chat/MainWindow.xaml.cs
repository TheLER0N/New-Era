using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly string _projectStatus;
    private string _mode;
    private bool _think;
    // Авторемонт: независимый от режима флаг «проверять проект после изменений».
    // Хранится в config в ProjectSettings[ключ проекта].AutoRepair (null = вкл).
    private bool _autoRepair = true;
    internal readonly List<Border> _interactiveCards = new();
    private readonly QwenBrowserPane _browserPane = QwenBrowserPane.Shared;

    private string HistoryKey => _projectPath != null
        ? HistoryStore.ProjectKey(_projectPath)
        : (_selectedRole ?? "unknown");

    // Ключ проекта в config — как у gateway: путь без хвостовых слэшей, в нижнем регистре.
    private string AutoRepairKey => _projectPath!.TrimEnd('\\', '/').ToLowerInvariant();

    public MainWindow(string? projectPath = null, string? projectName = null)
    {
        InitializeComponent();
        _browserPane.MountIn(BrowserPaneHost);
        _browserPane.CaptchaDetected += () =>
        {
            if (BrowserColumn.Width.Value < 1)
                OnToggleBrowserClick(this, new RoutedEventArgs());
            RoleStatus.Text = "⚠ Капча — пройди проверку в окне браузера.";
        };

        _projectPath = projectPath;
        _projectName = projectName;
        _mode = projectPath != null ? "edit" : "chat";
        UserProfile.Exists();
        _projectStatus = GetProjectStatus();
        _autoRepair = LoadAutoRepair();
        LoadHistoryFromDisk();

        InputBox.TextChanged += (_, _) =>
            InputPlaceholder.Visibility = string.IsNullOrEmpty(InputBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;

        KeyDown += OnMainWindowKeyDown;

        _waitTimer.Tick += (_, _) =>
        {
            var sec = (int)(DateTime.Now - _waitStart).TotalSeconds;
            if (_selectedRole != null)
                RoleStatus.Text = $"Ожидание ответа от {_selectedRole}... {sec}с";
        };

        var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        statusTimer.Tick += (_, _) => _ = LoadRolesStatusAsync();
        statusTimer.Start();
        _ = LoadRolesStatusAsync();

        RenderHistory();
        UpdateModeButtons();
        SessionProjectText.Text = _projectStatus;
    }

    internal static SolidColorBrush B(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private bool LoadAutoRepair()
    {
        if (_projectPath == null) return true;
        try
        {
            var configPath = BrowserLauncher.GetConfigPath();
            if (configPath == null) return true;
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            var v = node?["ProjectSettings"]?[AutoRepairKey]?["AutoRepair"];
            // null (не задано) = включено по умолчанию
            return v == null || v.GetValue<bool>();
        }
        catch { return true; }
    }

    private void SaveAutoRepair()
    {
        if (_projectPath == null) return;
        try
        {
            var configPath = BrowserLauncher.GetConfigPath();
            if (configPath == null) return;
            var node = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject();
            if (node == null) return;
            if (node["ProjectSettings"] is not JsonObject settings)
            {
                settings = new JsonObject();
                node["ProjectSettings"] = settings;
            }
            if (settings[AutoRepairKey] is not JsonObject proj)
            {
                proj = new JsonObject();
                settings[AutoRepairKey] = proj;
            }
            proj["AutoRepair"] = _autoRepair;
            File.WriteAllText(configPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private string GetProjectStatus()
    {
        if (_projectPath == null) return "—";
        try
        {
            var projects = ProjectStore.Load();
            var project = projects.FirstOrDefault(p =>
                string.Equals(p.Path.TrimEnd('\\', '/'), _projectPath.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase));
            var name = _projectName ?? project?.Name
                ?? new DirectoryInfo(_projectPath.TrimEnd('\\', '/')).Name;
            return $"{name} · {FormatRelative(project?.LastOpened ?? DateTime.Now)}";
        }
        catch { return _projectName ?? "—"; }
    }

    private static string FormatRelative(DateTime? dt)
    {
        if (dt == null) return "ещё не открыт";
        var span = DateTime.Now - dt.Value;
        if (span.TotalMinutes < 1) return "только что";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} мин назад";
        if (dt.Value.Date == DateTime.Today) return $"{(int)span.TotalHours} ч назад";
        if (dt.Value.Date == DateTime.Today.AddDays(-1)) return $"вчера, {dt:HH:mm}";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} дн назад";
        return dt.Value.ToString("dd.MM.yyyy");
    }

    private void OnToggleBrowserClick(object sender, RoutedEventArgs e)
    {
        bool show = BrowserColumn.Width.Value < 1;
        BrowserColumn.Width = show ? new GridLength(500) : new GridLength(0);
        ToggleBrowserBtn.Content = show ? "🌐 браузер: показан" : "🌐 браузер: скрыт";
    }

    private void OnModeAutoClick(object sender, RoutedEventArgs e) => SetMode("auto");
    private void OnModeYoloClick(object sender, RoutedEventArgs e) => SetMode("yolo");
    private void OnModeRepairClick(object sender, RoutedEventArgs e) => SetMode("repair");
    private void OnThinkClick(object sender, RoutedEventArgs e) => ToggleThink();

    private void OnAutoRepairClick(object sender, RoutedEventArgs e)
    {
        _autoRepair = !_autoRepair;
        SaveAutoRepair();
        UpdateModeButtons();
        RoleStatus.Text = _autoRepair
            ? "Авторемонт включён: проект проверяется после изменений."
            : "Авторемонт выключен: проверка проекта после изменений не запускается.";
    }

    private void ToggleThink()
    {
        _think = !_think;
        ThinkBtn.Content = _think ? "🧠 мышление" : "⚡ быстро";
        UpdateModeButtons();
        ApplyRoleSelection();
        _browserPane.SetThinkMode(_think);
    }

    private async System.Threading.Tasks.Task LoadRolesStatusAsync()
    {
        try
        {
            var resp = await _http.GetStringAsync("http://localhost:51234/status");
            var status = JsonSerializer.Deserialize<GatewayStatus>(
                resp, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (status == null) return;
            _boundRoles.Clear();
            if (status.Roles != null)
                foreach (var r in status.Roles) _boundRoles.Add(r);
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
        var user = string.IsNullOrWhiteSpace(UserProfile.Nick) ? "гость" : UserProfile.Nick;
        if (!bound)
        {
            RoleStatus.Text = $"👤 {user} · роль \"{_selectedRole}\" не закреплена за чатом. Открой чат Qwen и закрепи роль.";
            return;
        }
        var speed = _think ? "🧠 мышление" : "⚡ быстро";
        var proj = _projectPath != null ? $" · 📁 {_projectStatus}" : "";
        RoleStatus.Text = $"👤 {user} · роль: {_selectedRole} · {ModeLabel()} · {speed}{proj}";
        SessionRoleText.Text = _selectedRole;
        var label = ModeLabel();
        var sp = label.IndexOf(' ');
        SessionStyleText.Text = sp >= 0 ? label.Substring(sp + 1) : label;
        SessionSpeedText.Text = _think ? "мышление" : "быстро";
        SessionProjectText.Text = _projectStatus;
    }

    private string ModeLabel() => _mode switch
    {
        "plan" => "📋 планирование",
        "edit" => "✏️ аккуратный",
        "auto" => "🔄 авто",
        "yolo" => "⚡ агрессивный",
        "repair" => "🔧 ремонт",
        _ => "💬 чат"
    };

    private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.D1: case Key.NumPad1: SetMode("chat"); e.Handled = true; break;
            case Key.D2: case Key.NumPad2: SetMode("plan"); e.Handled = true; break;
            case Key.D3: case Key.NumPad3: SetMode("edit"); e.Handled = true; break;
            case Key.D4: case Key.NumPad4: SetMode("auto"); e.Handled = true; break;
            case Key.D5: case Key.NumPad5: SetMode("yolo"); e.Handled = true; break;
            case Key.D6: case Key.NumPad6: ToggleThink(); e.Handled = true; break;
            case Key.D7: case Key.NumPad7: SetMode("repair"); e.Handled = true; break;
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
        StyleModeButton(ModeRepairBtn, _mode == "repair");
        StyleModeButton(ThinkBtn, _think);
        // Тумблер авторемонта: не режим, а независимый флаг (зелёный = вкл).
        StyleModeButton(AutoRepairBtn, _autoRepair);
        AutoRepairBtn.Content = _autoRepair ? "🔧 авторемонт: вкл" : "🔧 авторемонт: выкл";
    }

    private static void StyleModeButton(Button btn, bool active)
    {
        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#123626" : "#0b1d14"));
        btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00ff88" : "#1d5c3d"));
        btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00ff88" : "#c8ffd8"));
    }

    // Прерванные вопросы не удаляем — помечаем «отменено» и гасим кнопки.
    internal void ClearInteractiveCards()
    {
        foreach (var c in _interactiveCards)
        {
            if (c.Tag is TextBlock tb)
            {
                tb.Text = "отменено";
                tb.Foreground = B("#e94560");
            }
            DisableButtons(c);
        }
        _interactiveCards.Clear();
    }

    // UIElementCollection — не-generic, поэтому явный тип UIElement в foreach.
    private static void DisableButtons(DependencyObject root)
    {
        if (root is Button b) { b.IsEnabled = false; return; }
        if (root is Panel p)
        {
            foreach (UIElement ch in p.Children) DisableButtons(ch);
            return;
        }
        if (root is Border bd && bd.Child != null) DisableButtons(bd.Child);
    }

    private void RenderHistory()
    {
        ChatMessages.Children.Clear();
        _interactiveCards.Clear();
        if (_history.TryGetValue(HistoryKey, out var list))
            foreach (var m in list)
                AddMessageBlock(m.Author, m.Text, m.Bg);
    }

    internal void AddMessage(string role, string author, string text, string bgColor)
    {
        var key = HistoryKey;
        if (!_history.ContainsKey(key)) _history[key] = new();
        _history[key].Add(new ChatMessage { Author = author, Text = text, Bg = bgColor });
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

        var time = new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontFamily = Theme.Font(), FontSize = 12,
            Foreground = B("#447a5a"), Margin = new Thickness(4, 0, 0, 4)
        };
        Grid.SetRow(time, 0); Grid.SetColumn(time, 1);
        row.Children.Add(time);

        var avatar = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            Background = B("#0d2418"),
            BorderBrush = isUser ? B("#00ff88") : B("#1d5c3d"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        if (isUser)
        {
            avatar.Child = new System.Windows.Shapes.Path
            {
                Fill = B("#00ff88"), Width = 16, Height = 16,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Data = Geometry.Parse(
                    "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z")
            };
        }
        else
        {
            avatar.Child = new TextBlock
            {
                Text = ">_", FontFamily = Theme.Font(), FontSize = 15,
                FontWeight = FontWeights.Bold, Foreground = B("#7dffa8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetRow(avatar, 1); Grid.SetColumn(avatar, 0);
        row.Children.Add(avatar);

        var bubble = new Border
        {
            Background = B("#0a1a12"),
            BorderBrush = isUser ? B("#00ff88") : B(bg),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 10),
            Margin = new Thickness(4, 0, 0, 0)
        };
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = author, FontFamily = Theme.Font(), FontSize = 11,
            Foreground = isUser ? B("#00ff88") : B("#447a5a"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        inner.Children.Add(new TextBlock
        {
            Text = text, FontFamily = Theme.Font(), FontSize = 14,
            Foreground = B("#d9ffe7"), TextWrapping = TextWrapping.Wrap
        });
        bubble.Child = inner;
        Grid.SetRow(bubble, 1); Grid.SetColumn(bubble, 1);
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
        if (_waiting) { _ = CancelWait(); return; }
        SendMessage();
    }

    private async System.Threading.Tasks.Task CancelWait()
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { role = _selectedRole });
            await _http.PostAsync("http://localhost:51234/cancel",
                new StringContent(payload, Encoding.UTF8, "application/json"));
        }
        catch { }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        var hub = new ProjectHubWindow();
        hub.StartFullscreen = false;
        hub.WindowStartupLocation = WindowStartupLocation.Manual;
        hub.Left = Left; hub.Top = Top; hub.Width = Width; hub.Height = Height;
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
        ClearInteractiveCards();

        SendBtn.IsEnabled = true;
        SendBtn.Content = "⏹ стоп";
        _waiting = true;
        _waitStart = DateTime.Now;
        _waitTimer.Start();

        try
        {
            var agentPayload = JsonSerializer.Serialize(new
            {
                role, text,
                projectPath = _projectPath,
                mode = _mode,
                think = _think,
                autoRepair = _autoRepair
            });
            var agentResp = await _http.PostAsync("http://localhost:51234/agent-run",
                new StringContent(agentPayload, Encoding.UTF8, "application/json"));
            var agentBody = await agentResp.Content.ReadAsStringAsync();
            if (agentResp.IsSuccessStatusCode)
                await HandleAgentBody(role, agentBody);
            else
                AddMessage(role, "Ошибка", ShortError(agentBody), "#e94560");
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

    internal static string ShortError(string body)
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

    private async System.Threading.Tasks.Task HandleAgentBody(string role, string body)
    {
        var r = DeserializeAgent(body);
        if (r == null)
        {
            AddMessage(role, "Ошибка", "Не удалось разобрать ответ агента.", "#e94560");
            return;
        }

        switch (r.Status)
        {
            case "final":
            {
                // действия — до текста ответа, итог-карточка — после
                RenderCards(r.Cards?.Where(c => c.Type != "summary").ToList());
                if (!string.IsNullOrWhiteSpace(r.Response))
                    AddMessage(role, role, r.Response, "#16213e");
                var summary = r.Cards?.Where(c => c.Type == "summary").ToList();
                // Если авторемонт выключен, файлы менялись, а режим не «ремонт» —
                // проверка проекта не запускалась: помечаем итог.
                if (summary != null && !_autoRepair && _mode != "repair" && (r.ChangedFiles?.Count ?? 0) > 0)
                {
                    foreach (var c in summary)
                        c.Details += (string.IsNullOrEmpty(c.Details) ? "" : "\n") +
                                     "⚠ авторемонт выключен — проверка проекта не запускалась";
                }
                RenderCards(summary);
                break;
            }
            case "approval": AddApprovalCard(r); break;
            case "user_input": AddUserInputCard(r); break;
            case "more_steps": AddMoreStepsCard(r); break;
            case "outside_access": AddOutsideCard(r); break;
            default: AddMessage(role, role, r.Response ?? body, "#16213e"); break;
        }
    }

    internal static AgentRunResponse? DeserializeAgent(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<AgentRunResponse>(
                body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    internal async System.Threading.Tasks.Task PostApprovalAsync(object payload)
    {
        try
        {
            var resp = await _http.PostAsync("http://localhost:51234/agent-approve",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
                await HandleAgentBody(_selectedRole ?? "coder", body);
            else
                AddMessage(_selectedRole ?? "система", "Ошибка", ShortError(body), "#e94560");
        }
        catch (Exception ex)
        {
            AddMessage(_selectedRole ?? "система", "Ошибка", ex.Message, "#e94560");
        }
    }

    internal void LoadHistoryFromDisk() => _history = HistoryStore.Load();
    internal void SaveHistoryToDisk() => HistoryStore.Save(_history);
}