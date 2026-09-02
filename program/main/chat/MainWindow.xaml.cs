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
    private bool _autoRepair = true;
    internal readonly List<Border> _interactiveCards = new();
    private readonly QwenBrowserPane _browserPane = QwenBrowserPane.Shared;
    private int _lastStepUsed = 0;
    private int _lastStepLimit = 30;

    private string HistoryKey => _projectPath != null
        ? HistoryStore.ProjectKey(_projectPath)
        : (_selectedRole ?? "unknown");

    private string AutoRepairKey => _projectPath!.TrimEnd('\\', '/').ToLowerInvariant();

    public MainWindow(string? projectPath = null, string? projectName = null)
    {
        InitializeComponent();
        MemoryPageControl.BackRequested += () => { MemoryOverlay.Visibility = Visibility.Collapsed; }; LoadBoundRolesFromConfig();
        var holo = Theme.MakeHoloPlanet(); Grid.SetRow(holo, 1); ChatRootGrid.Children.Insert(0, holo);

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

        SessionPathText.Text = _projectPath ?? "—";
        SessionProjectText.Text = _projectStatus;

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
        UpdateStepCounter();
    }

private static FrameworkElement WithCorners(Border b, bool user)
{
var g = new Grid();
g.Children.Add(b);
var c = user ? B("#00d9ff") : B("#1f6f86");
g.Children.Add(new Border { Width = 10, Height = 10, BorderBrush = c, BorderThickness = new Thickness(2, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
g.Children.Add(new Border { Width = 10, Height = 10, BorderBrush = c, BorderThickness = new Thickness(0, 2, 2, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
g.Children.Add(new Border { Width = 10, Height = 10, BorderBrush = c, BorderThickness = new Thickness(2, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });
g.Children.Add(new Border { Width = 10, Height = 10, BorderBrush = c, BorderThickness = new Thickness(0, 0, 2, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom });
return g;
}internal static SolidColorBrush B(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    // Буфер обмена: Clipboard.SetText бросает COM-исключение, если
    // буфер занят другим процессом — глотаем, чтобы не ронять чат.
    internal static void SafeCopy(string s)
    {
        try { Clipboard.SetText(s); } catch { }
    }

    private bool LoadAutoRepair()
    {
        if (_projectPath == null) return true;
        try
        {
            var configPath = BrowserLauncher.GetConfigPath();
            if (configPath == null) return true;
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            var v = node?["ProjectSettings"]?[AutoRepairKey]?["AutoRepair"];
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
            var dirName = new DirectoryInfo(_projectPath.TrimEnd('\\', '/')).Name;
            var name = _projectName ?? dirName;
            var projects = ProjectStore.Load();
            var project = projects.FirstOrDefault(p =>
                string.Equals(p.Path.TrimEnd('\\', '/'), _projectPath.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase));
            var lastOpened = project?.LastOpened;
            var timeStr = FormatRelative(lastOpened);
            return $"{name} · {timeStr}";
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
        if (show)
        {
            BrowserColumn.Width = new GridLength(520);
            BrowserSplitter.Visibility = Visibility.Visible;
        }
        else
        {
            BrowserColumn.Width = new GridLength(0);
            BrowserSplitter.Visibility = Visibility.Collapsed;
        }
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
                OnlineDot.Fill = B("#00d9ff");
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

    
private void LoadBoundRolesFromConfig()
{
    _boundRoles.Clear();
    try {
        var path = BrowserLauncher.GetConfigPath();
        if (path == null || !File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.TryGetProperty("Roles", out var roles))
            foreach (var r in roles.EnumerateObject())
                if (r.Value.TryGetProperty("ChatId", out var cid) && !string.IsNullOrEmpty(cid.GetString()))
                    _boundRoles.Add(r.Name);
    } catch {}
}
private void ApplyRoleSelection()
    {
        if (_selectedRole == null) return;
        var bound = _boundRoles.Contains(_selectedRole);
        InputBox.IsEnabled = true;
        SendBtn.IsEnabled = true;
        var user = string.IsNullOrWhiteSpace(UserProfile.Nick) ? "гость" : UserProfile.Nick;
        if (!bound) { RoleStatus.Text = "Роль не закреплена — писать всё равно можно."; }
        var speed = _think ? "🧠 мышление" : "⚡ быстро";
        var proj = _projectPath != null ? $" · 📁 {_projectStatus}" : "";
        RoleStatus.Text = $"👤 {user} · роль: {_selectedRole} · {ModeLabel()} · {speed}{proj}";
        SessionRoleText.Text = _selectedRole;
        var label = ModeLabel();
        var sp = label.IndexOf(' ');
        SessionStyleText.Text = sp >= 0 ? label.Substring(sp + 1) : label;
        SessionSpeedText.Text = _think ? "мышление" : "быстро";
        SessionProjectText.Text = _projectStatus;
        SessionPathText.Text = _projectPath ?? "—";
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
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
switch (e.Key)
{ case Key.D1: case Key.NumPad1: SetMode("chat"); e.Handled = true; break;
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
        StyleModeButton(AutoRepairBtn, _autoRepair);
        AutoRepairBtn.Content = _autoRepair ? "🔧 авторемонт: вкл" : "🔧 авторемонт: выкл";
    }

    private static void StyleModeButton(Button btn, bool active)
    {
        btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#12404f" : "#0a1c28"));
        btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00d9ff" : "#12404f"));
        btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#00d9ff" : "#eaf6ff"));
    }

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
            {
                AddMessageBlock(m.Author, m.Text, m.Bg, m.Time);
                if (!string.IsNullOrEmpty(m.CardsJson))
                {
                    try
                    {
                        var cards = JsonSerializer.Deserialize<List<ActionCardDto>>(m.CardsJson,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (cards != null) RenderCards(cards);
                    }
                    catch { }
                }
            }
    }

    internal void AddMessage(string role, string author, string text, string bgColor, string? cardsJson = null)
    {
        var key = HistoryKey;
        if (!_history.ContainsKey(key)) _history[key] = new();
        var time = DateTime.Now.ToString("HH:mm");
        _history[key].Add(new ChatMessage
        {
            Author = author,
            Text = text,
            Bg = bgColor,
            Time = time,
            CardsJson = cardsJson
        });
        SaveHistoryToDisk();
        AddMessageBlock(author, text, bgColor, time);
    }

    private void AddMessageBlock(string author, string text, string bg, string time)
    {
        bool isUser = author.Trim().Equals("ТЫ", StringComparison.OrdinalIgnoreCase);
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var timeBlock = new TextBlock
        {
            Text = string.IsNullOrEmpty(time) ? DateTime.Now.ToString("HH:mm") : time,
            FontFamily = Theme.Font(),
            FontSize = 15,
            Foreground = B("#6f96a8"),
            Margin = new Thickness(4, 0, 0, 4)
        };
        Grid.SetRow(timeBlock, 0); Grid.SetColumn(timeBlock, 1);
        row.Children.Add(timeBlock);

        var avatar = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(24),
            Background = B("#0a1c28"),
            BorderBrush = isUser ? B("#00d9ff") : B("#12404f"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        if (isUser)
        {
            avatar.Child = new System.Windows.Shapes.Path
            {
                Fill = B("#00d9ff"), Width = 20, Height = 20,
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
                Text = ">_", FontFamily = Theme.Font(), FontSize = 17,
                FontWeight = FontWeights.Bold, Foreground = B("#8fe6ff"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetRow(avatar, 1); Grid.SetColumn(avatar, 0);
        row.Children.Add(avatar);

        var bubble = new Border
        {
            Background = B("#07141d"),
            BorderBrush = isUser ? B("#1f6f86") : B(bg),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),Padding = new Thickness(16, 12, 16, 14),
        };
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = author, FontFamily = Theme.Font(), FontSize = 14,
            Foreground = isUser ? B("#00d9ff") : B("#6f96a8"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        // Текст сообщения — выделяемый read-only TextBox:
        // выделяй мышью и копируй Ctrl+C или ПКМ → Копировать.
        // ИСПРАВЛЕНИЕ CS0029: в C# нет неявного int→Thickness,
        // поэтому BorderThickness/Padding только через new Thickness(...).
        inner.Children.Add(new TextBox
        {
            Text = text,
            FontFamily = Theme.Font(),
            FontSize = 17,
            Foreground = B("#eaf6ff"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0),
            CaretBrush = B("#00d9ff"),
            SelectionBrush = B("#12404f"),
            IsTabStop = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        bubble.Child = inner;

        // ПКМ по пузырю — скопировать сообщение целиком.
        var copyAll = new MenuItem { Header = "Копировать сообщение" };
        copyAll.Click += (_, _) => SafeCopy(text);
        bubble.ContextMenu = new ContextMenu();
        bubble.ContextMenu.Items.Add(copyAll);

        var wrap = WithCorners(bubble, isUser); Grid.SetRow(wrap, 1); Grid.SetColumn(wrap, 1); wrap.HorizontalAlignment = isUser ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; wrap.Margin = new Thickness(4, 0, 0, 0); row.Children.Add(wrap);
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

    private void SendMessage()
    {
        if (_waiting) return;
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || _selectedRole == null) return;
        var role = _selectedRole;
        AddMessage(role, "Ты", text, "#0f3460");
        InputBox.Clear();
        ClearInteractiveCards();

        // Планирование: сначала карточка настроек, запрос уходит по клику.
        if (_mode == "plan")
        {
            AddPlanSettingsCard(role, text);
            return;
        }

        _ = SendAgentRunAsync(role, text, null, null, null);
    }

    // planRounds/planMin/planMax уходят в /agent-run только в режиме «планирование».
    internal async System.Threading.Tasks.Task SendAgentRunAsync(
        string role, string text, int? planRounds, int? planMin, int? planMax)
    {
        SendBtn.IsEnabled = true;
        SendBtn.Content = "⏹ стоп";
        _waiting = true;
        _waitStart = DateTime.Now;
        _waitTimer.Start();
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["role"] = role,
                ["text"] = text,
                ["projectPath"] = _projectPath,
                ["mode"] = _mode,
                ["think"] = _think,
                ["autoRepair"] = _autoRepair
            };
            if (planRounds.HasValue) payload["planRounds"] = planRounds.Value;
            if (planMin.HasValue) payload["planMin"] = planMin.Value;
            if (planMax.HasValue) payload["planMax"] = planMax.Value;

            var agentResp = await _http.PostAsync("http://localhost:51234/agent-run",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
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

    private void UpdateStepCounter()
    {
        Dispatcher.InvokeAsync(() =>
        {
            StepCounterText.Text = $"{_lastStepUsed}/{_lastStepLimit}";
            if (_lastStepUsed >= _lastStepLimit)
                StepCounterText.Foreground = B("#e94560");
            else if (_lastStepUsed >= _lastStepLimit * 0.9)
                StepCounterText.Foreground = B("#ffb14a");
            else
                StepCounterText.Foreground = B("#00d9ff");
        });
    }

    private void UpdateStepsFromJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("stepsUsed", out var su))
                _lastStepUsed = su.GetInt32();
            if (doc.RootElement.TryGetProperty("stepLimit", out var sl))
                _lastStepLimit = sl.GetInt32();
        }
        catch { }
        UpdateStepCounter();
    }

    private async System.Threading.Tasks.Task HandleAgentBody(string role, string body)
    {
        UpdateStepsFromJson(body);
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
                RenderCards(r.Cards?.Where(c => c.Type != "summary").ToList());
                if (!string.IsNullOrWhiteSpace(r.Response))
                {
                    var cardsJson = r.Cards != null && r.Cards.Count > 0
                        ? JsonSerializer.Serialize(r.Cards) : null;
                    AddMessage(role, role, r.Response, "#16213e", cardsJson);
                }
                var summary = r.Cards?.Where(c => c.Type == "summary").ToList();
                if (summary != null && !_autoRepair && _mode != "repair" && (r.ChangedFiles?.Count ?? 0) > 0)
                {
                    foreach (var c in summary)
                        c.Details += (string.IsNullOrEmpty(c.Details) ? "" : "\n") +
                            "⚠ авторемонт выключен — проверка проекта не запускалась";
                }
                RenderCards(summary);
                // План готов: предложить реализовать его в авто-режиме.
                if (_mode == "plan" && !string.IsNullOrWhiteSpace(r.Response))
                    AddPlanDoneCard(role, r.Response);
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

    private void OnMemoryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MemoryOverlay.Visibility == Visibility.Visible)
            {
                MemoryOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                var root = _projectPath ?? "";
                if (!string.IsNullOrEmpty(root))
                {
                    MemoryPageControl.Load(root);
                    MemoryOverlay.Visibility = Visibility.Visible;
                }
            }
        }
        catch { }
    }
}