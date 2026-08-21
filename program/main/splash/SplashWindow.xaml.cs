using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MainApp;

public partial class SplashWindow : ChromeWindow
{
    private enum Stage { Boot, AskNick, AskAbout, Greet, Loading, Done }
    private Stage _stage = Stage.Boot;
    private string _nick = "";
    private bool _ready;
    private bool _readyOk;
    private bool _transitioning;
    private bool _crtStarted;
    private DispatcherTimer? _blackTimer;
    private double _progress;
    private string _printed = "";
    private string? _cur;
    private int _pos;
    private Action? _done;
    private readonly Queue<(string text, Action? done)> _q = new();
    private readonly DispatcherTimer _typeTimer = new() { Interval = TimeSpan.FromMilliseconds(12) };
    private readonly DispatcherTimer _barTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };

    public SplashWindow()
    {
        FxIntensity = 0.7;
        UseFadeIn = false;
        InitializeComponent();
        QwenBrowserPane.EnsureOffscreen();
        Loaded += OnLoaded;
        _typeTimer.Tick += OnTypeTick;
        _barTimer.Tick += (_, _) =>
        {
            if (_ready) return;
            _progress = Math.Min(0.92, _progress + (0.92 - _progress) * 0.04 + 0.0015);
            DrawBar();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && !InputBox.IsFocused)
        {
            e.Handled = true;
            if (_blackTimer != null && _blackTimer.IsEnabled) { _blackTimer.Stop(); StartCrt(); }
            else if (_stage == Stage.Loading && _ready && !_readyOk) BeginTransition();
            else FlushTyper();
        }
        base.OnKeyDown(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Children.Add(Theme.MakeFx(0.7));
        FooterLeft.Text = $"© 2026 LERON SYSTEMS // SESSION 0x{Environment.TickCount & 0xFFFF:X4}";
        FooterRight.Text = $"USER: {(UserProfile.Exists() ? UserProfile.Nick : "GUEST")} // TTY1";
        SessionUser.Text = UserProfile.Exists() ? UserProfile.Nick : "—";
        FillSessionFromConfig();
        Diag("gateway 51234 ..... START");
        _ = GatewayLauncher.EnsureRunningAsync();
        _ = QwenBrowserPane.Shared.ReadyTask.ContinueWith(t =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                Diag(t.Result ? "webview2 ........ OK" : "webview2 ........ FAIL");
                Diag(t.Result ? "qwen session .... LOADED" : "qwen session .... FAIL");
                Diag(t.Result ? "модель .......... 3.8-MAX" : "модель .......... —");
                OnBrowserReady(t.Result);
            });
        });

        _blackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _blackTimer.Tick += (_, _) => { _blackTimer.Stop(); StartCrt(); };
        _blackTimer.Start();
    }

    private void StartCrt()
    {
        if (_crtStarted) return;
        _crtStarted = true;
        RootGrid.Children.Remove(BlackStart);
        var skip = Theme.Crt.PowerOn(RootGrid, () =>
        {
            TypeLine("> LERON BIOS v2.6 — POST", () =>
            {
                TypeLine("> PHOSPHOR P1-GREEN .... OK", () =>
                {
                    if (UserProfile.Exists()) StartGreet();
                    else TypeLine("> первый запуск · регистрация пользователя", AskNick);
                });
            });
        });
        HookSkip(this, skip);
    }

        private void BeginTransition()
    {
        if (_transitioning) return;
        _transitioning = true;
        _stage = Stage.Done;
        Topmost = true;

        var next = new ProjectHubWindow();
        next.UseFadeIn = false;
        next.Opacity = 0;
        next.Show();

        // CRT выключение только для сплэша
        Theme.Crt.PowerOff(RootGrid, () =>
        {
            Close();
            // Хаб появляется плавно
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            next.BeginAnimation(OpacityProperty, fadeIn);
        });
    }

    private void HookSkip(Window w, Action skip)
    {
        void OnSkip(object s, EventArgs e) { skip(); w.KeyDown -= OnSkip; w.MouseDown -= OnSkip; }
        w.KeyDown += OnSkip;
        w.MouseDown += OnSkip;
    }

    private void Diag(string line) { DiagLog.Text += line + "\n"; }

    private void FillSessionFromConfig()
    {
        try
        {
            var path = BrowserLauncher.GetConfigPath();
            if (path != null)
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                var arr = node?["Projects"] as JsonArray;
                SessionProject.Text = arr != null && arr.Count > 0 ? arr[arr.Count - 1]?["Name"]?.GetValue<string>() ?? "—" : "—";
            }
            var hist = HistoryStore.Load();
            int total = 0;
            foreach (var kv in hist) total += kv.Value.Count;
            SessionHistory.Text = total + " сообщений";
        }
        catch { }
    }

    private void AskNick() { _stage = Stage.AskNick; Print("> введи ник: "); InputLine.Visibility = Visibility.Visible; InputBox.Focus(); }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = InputBox.Text.Trim();
        if (_stage == Stage.AskNick)
        {
            if (text.Length == 0) { Print("  ⚠ ник не может быть пустым"); return; }
            _nick = text; Print("  ✓ ник: " + _nick); InputBox.Clear();
            _stage = Stage.AskAbout; Print("> расскажи о себе в паре слов: ");
        }
        else if (_stage == Stage.AskAbout)
        {
            UserProfile.Save(_nick, text); Print("  ✓ профиль сохранён");
            InputLine.Visibility = Visibility.Collapsed; SessionUser.Text = _nick;
            FooterRight.Text = $"USER: {_nick} // TTY1"; StartGreet();
        }
    }

    private void StartGreet()
    {
        _stage = Stage.Greet;
        TypeLine($"LERON GUI приветствует пользователя {UserProfile.Nick}!", () =>
        {
            _stage = Stage.Loading; DrawBar(); _barTimer.Start();
            if (_ready) OnBrowserReady(_readyOk);
        });
    }

    private void OnBrowserReady(bool ok)
    {
        _ready = true; _readyOk = ok;
        if (_stage != Stage.Loading) return;
        if (ok)
        {
            _progress = 1; DrawBar(); _barTimer.Stop();
            TypeLine("> браузер готов · вход в систему", () =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                t.Tick += (_, _) => { t.Stop(); BeginTransition(); }; t.Start();
            });
        }
        else { _barTimer.Stop(); PercentText.Text = "ERR"; Print("⚠ браузер не загрузился · [пробел] — войти без него"); }
    }

    private void DrawBar()
    {
        const int cells = 24; int done = (int)Math.Round(_progress * cells);
        var sb = new StringBuilder();
        for (int i = 0; i < cells; i++) sb.Append(i < done ? "▮ " : "░ ");
        BarText.Text = sb.ToString(); PercentText.Text = (int)(_progress * 100) + "%";
    }

    private void Print(string line) { _printed += line + "\n"; TermText.Text = _printed; TermScroll.ScrollToEnd(); }

    private void TypeLine(string text, Action? done = null) { _q.Enqueue((text, done)); if (!_typeTimer.IsEnabled) _typeTimer.Start(); }

    private void OnTypeTick(object? s, EventArgs e)
    {
        if (_cur == null)
        {
            if (_q.Count == 0) { _typeTimer.Stop(); return; }
            var next = _q.Dequeue(); _cur = next.text; _pos = 0; _done = next.done;
        }
        _pos = Math.Min(_cur.Length, _pos + 2);
        TermText.Text = _printed + _cur.Substring(0, _pos);
        if (_pos >= _cur.Length)
        {
            _printed += _cur + "\n"; TermText.Text = _printed;
            var d = _done; _cur = null; _done = null; d?.Invoke();
        }
        TermScroll.ScrollToEnd();
    }

    private void FlushTyper()
    {
        while (_cur != null || _q.Count > 0)
        {
            if (_cur == null) { var n = _q.Dequeue(); _printed += n.text + "\n"; n.done?.Invoke(); }
            else { _printed += _cur + "\n"; _done?.Invoke(); _cur = null; _done = null; }
        }
        _typeTimer.Stop(); TermText.Text = _printed; TermScroll.ScrollToEnd();
    }
}

public static class UserProfile
{
    public static string Nick = "";
    public static string About = "";
    public static bool Exists()
    {
        try
        {
            var path = BrowserLauncher.GetConfigPath();
            if (path == null) return false;
            var node = JsonNode.Parse(File.ReadAllText(path));
            var p = node?["UserProfile"];
            Nick = p?["Nick"]?.GetValue<string>() ?? "";
            About = p?["About"]?.GetValue<string>() ?? "";
            return !string.IsNullOrWhiteSpace(Nick);
        }
        catch { return false; }
    }
    public static void Save(string nick, string about)
    {
        Nick = nick; About = about;
        try
        {
            var path = BrowserLauncher.GetConfigPath();
            if (path == null) return;
            var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            if (node == null) return;
            node["UserProfile"] = new JsonObject { ["Nick"] = nick, ["About"] = about };
            File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}