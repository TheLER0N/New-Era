using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
if (_stage == Stage.Loading && _ready && !_readyOk) BeginTransition();
else FlushTyper();
}
base.OnKeyDown(e);
}
private void OnLoaded(object sender, RoutedEventArgs e)
{
Theme.PowerOn(this);
RootGrid.Children.Add(Theme.MakeFx(0.7));
_ = GatewayLauncher.EnsureRunningAsync();
_ = QwenBrowserPane.Shared.ReadyTask.ContinueWith(t =>
Dispatcher.InvokeAsync(() => OnBrowserReady(t.Result)));
TypeLine("> LERON BIOS v2.6 — POST", () =>
{
TypeLine("> MEM 65536 KB ......... OK", () =>
{
TypeLine("> PHOSPHOR P1-GREEN .... OK", () =>
{
TypeLine("> GATEWAY LINK ......... SYNC", () =>
{
if (UserProfile.Exists()) StartGreet();
else TypeLine("> первый запуск · регистрация пользователя", AskNick);
});
});
});
});
}
private void AskNick()
{
_stage = Stage.AskNick;
Print("> введи ник: ");
InputLine.Visibility = Visibility.Visible;
InputBox.Focus();
}
private void OnInputKeyDown(object sender, KeyEventArgs e)
{
if (e.Key != Key.Enter) return;
e.Handled = true;
var text = InputBox.Text.Trim();
if (_stage == Stage.AskNick)
{
if (text.Length == 0) { Print("  ⚠ ник не может быть пустым"); return; }
_nick = text;
Print("  ✓ ник: " + _nick);
InputBox.Clear();
_stage = Stage.AskAbout;
Print("> расскажи о себе в паре слов: ");
}
else if (_stage == Stage.AskAbout)
{
UserProfile.Save(_nick, text);
Print("  ✓ профиль сохранён");
InputLine.Visibility = Visibility.Collapsed;
StartGreet();
}
}
private void StartGreet()
{
_stage = Stage.Greet;
TypeLine($"LERON GUI приветствует пользователя {UserProfile.Nick}!", () =>
{
_stage = Stage.Loading;
BarText.Visibility = Visibility.Visible;
DrawBar();
_barTimer.Start();
if (_ready) OnBrowserReady(_readyOk);
});
}
private void OnBrowserReady(bool ok)
{
_ready = true;
_readyOk = ok;
if (_stage != Stage.Loading) return;
if (ok)
{
_progress = 1;
DrawBar();
_barTimer.Stop();
TypeLine("> браузер готов · вход в систему", () =>
{
var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
t.Tick += (_, _) => { t.Stop(); BeginTransition(); };
t.Start();
});
}
else
{
_barTimer.Stop();
BarText.Text = "[ браузер offline ]";
Print("⚠ браузер не загрузился · [пробел] — войти без него");
}
}
private void DrawBar()
{
const int cells = 40;
int done = (int)Math.Round(_progress * cells);
BarText.Text = "[" + new string('█', done) + new string('░', cells - done) + "] " + (int)(_progress * 100) + "%";
}
private void Print(string line)
{
_printed += line + "\n";
TermText.Text = _printed;
TermScroll.ScrollToEnd();
}
private void TypeLine(string text, Action? done = null)
{
_q.Enqueue((text, done));
if (!_typeTimer.IsEnabled) _typeTimer.Start();
}
private void OnTypeTick(object? s, EventArgs e)
{
if (_cur == null)
{
if (_q.Count == 0) { _typeTimer.Stop(); return; }
var next = _q.Dequeue();
_cur = next.text;
_pos = 0;
_done = next.done;
}
_pos = Math.Min(_cur.Length, _pos + 2);
TermText.Text = _printed + _cur.Substring(0, _pos);
if (_pos >= _cur.Length)
{
_printed += _cur + "\n";
TermText.Text = _printed;
var d = _done;
_cur = null;
_done = null;
d?.Invoke();
}
TermScroll.ScrollToEnd();
}
private void FlushTyper()
{
while (_cur != null || _q.Count > 0)
{
if (_cur == null)
{
var n = _q.Dequeue();
_printed += n.text + "\n";
n.done?.Invoke();
}
else
{
_printed += _cur + "\n";
_done?.Invoke();
_cur = null;
_done = null;
}
}
_typeTimer.Stop();
TermText.Text = _printed;
TermScroll.ScrollToEnd();
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
var off = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
off.Tick += (_, _) =>
{
off.Stop();
Theme.PowerOff(this, () =>
{
next.Opacity = 1;
Topmost = false;
Close();
});
};
off.Start();
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
Nick = nick;
About = about;
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