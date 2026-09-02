using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MainApp;

namespace Hub
{
    public partial class HubSettingsPage : UserControl
    {
        public event Action? BackRequested;

        private sealed class TestDef
        {
            public string Name = ""; public string Icon = "";
            public Func<Task<bool>> Run = () => Task.FromResult(true);
            public TextBlock? TimeText; public Border? Badge; public TextBlock? BadgeText;
        }

        private readonly List<TestDef> _tests = new();
        private string _initialUsername = "", _initialDescription = "";
        private readonly DateTime _sessionStart = DateTime.Now;
        private readonly DispatcherTimer _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private int _tick;
        private bool _testRunning;
        private string _crumb = "";

        public HubSettingsPage()
        {
            InitializeComponent();
            Loaded += (_, _) => { Focus(); SetupTests(); InitDashboard(); };
            KeyDown += OnKeyDown;
            _uiTimer.Tick += (_, _) => OnUiTick();
        }

        private static SolidColorBrush B(string h) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
        private static string LogsDir() { var d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"); try { Directory.CreateDirectory(d); } catch { } return d; }
        private static void TestLog(string file, string action, string input, bool ok, string err)
        { try { File.AppendAllText(Path.Combine(LogsDir(), file), $"[{DateTime.Now:HH:mm:ss}] {action} | вход: {input} | результат: {(ok ? "OK" : "ОШИБКА " + err)}\n", Encoding.UTF8); } catch { } }

        // ── инициализация дашборда ─────────────────────────────
        public void Reload()
        {
            try
            {
                UserProfile.Exists();
                TxtUsername.Text = UserProfile.Nick ?? "";
                TxtDescription.Text = UserProfile.Description ?? "";
            }
            catch { }
            _initialUsername = TxtUsername.Text;
            _initialDescription = TxtDescription.Text;
            ApplyAvatar();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"версия v{v.Major}.{v.Minor}.{v.Build}";
            DiagLog.Text = $"сборка: v{v.Major}.{v.Minor}.{v.Build}\nпапка: {AppDomain.CurrentDomain.BaseDirectory}\nлоги: {LogsDir()}";
            RefreshLogs();
            UpdateStats();
            UpdateCrumb();
        }

        private void InitDashboard()
        {
            try
            {
                if (UserProfile.Exists())
                {
                    if (string.IsNullOrWhiteSpace(TxtUsername.Text)) TxtUsername.Text = UserProfile.Nick;
                    if (string.IsNullOrWhiteSpace(TxtDescription.Text)) TxtDescription.Text = UserProfile.Description;
                }
            }
            catch { }
            _initialUsername = TxtUsername.Text; _initialDescription = TxtDescription.Text;

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"версия v{v.Major}.{v.Minor}.{v.Build}";
            DiagLog.Text = $"сборка: v{v.Major}.{v.Minor}.{v.Build}\nпапка: {AppDomain.CurrentDomain.BaseDirectory}\nлоги: {LogsDir()}";

            ApplyAvatar();
            RefreshLogs();
            UpdateStats();
            SwitchPanel("Profile");
            AddActivity("⚡", "вход в систему", "#7d93a5");
            _uiTimer.Start();
        }

        private void OnUiTick()
        {
            try { SessionTimeText.Text = (DateTime.Now - _sessionStart).ToString(@"hh\:mm\:ss"); } catch { }
            _tick++;
            if (_tick % 5 == 0) { UpdateStats(); }
            UpdateCrumb();
        }

        // ── профиль (без дубля в шапке) ────────────────────────
        private void ApplyAvatar()
        {
            var n = TxtUsername.Text.Trim();
            var l = string.IsNullOrWhiteSpace(n) ? "U" : n.Substring(0, 1).ToUpper();
            ProfileAvatarLetter.Text = l;
            ProfileName.Text = n == "" ? "User" : n;
            ProfileRole.Text = string.IsNullOrWhiteSpace(TxtDescription.Text) ? "оператор LERON" : TxtDescription.Text;
            FooterNick.Text = "загружено: " + (n == "" ? "User" : n);
        }
        private void TxtUsername_TextChanged(object sender, TextChangedEventArgs e) => ApplyAvatar();
        private void TxtDescription_TextChanged(object sender, TextChangedEventArgs e) => ApplyAvatar();

        // ── навигация ──────────────────────────────────────────
        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string t) SwitchPanel(t);
        }
        private void BtnBack_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

        private void SwitchPanel(string n)
        {
            PanelProfile.Visibility = n == "Profile" ? Visibility.Visible : Visibility.Collapsed;
            PanelTools.Visibility = n == "Tools" ? Visibility.Visible : Visibility.Collapsed;
            PanelTests.Visibility = n == "Tests" ? Visibility.Visible : Visibility.Collapsed;
            PanelLogs.Visibility = n == "Logs" ? Visibility.Visible : Visibility.Collapsed;
            PanelHotkeys.Visibility = n == "Hotkeys" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = n == "About" ? Visibility.Visible : Visibility.Collapsed;

            var map = new Dictionary<string, Button>
            {
                {"Profile", MenuProfile}, {"Tools", MenuTools}, {"Tests", MenuTests},
                {"Logs", MenuLogs}, {"Hotkeys", MenuHotkeys}, {"About", MenuAbout}
            };
            foreach (var kv in map)
            {
                kv.Value.Background = kv.Key == n ? B("#0d2231") : B("#00000000");
                kv.Value.Foreground = kv.Key == n ? B("#00d9ff") : B("#7d93a5");
            }
            UpdateCrumb();
        }

        private void UpdateCrumb()
        {
            string cur = "Профиль";
            if (PanelTools.Visibility == Visibility.Visible) cur = "Инструменты";
            else if (PanelTests.Visibility == Visibility.Visible) cur = "Тесты";
            else if (PanelLogs.Visibility == Visibility.Visible) cur = "Логи";
            else if (PanelHotkeys.Visibility == Visibility.Visible) cur = "Горячие клавиши";
            else if (PanelAbout.Visibility == Visibility.Visible) cur = "О системе";
            var line = $"Главная / Настройки / {cur}";
            if (line != _crumb) { _crumb = line; CrumbText.Text = line; }
        }

        // ── поиск по меню ──────────────────────────────────────
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = (SearchBox.Text ?? "").Trim().ToLower();
            foreach (var btn in new[] { MenuProfile, MenuTools, MenuTests, MenuLogs, MenuHotkeys, MenuAbout })
            {
                var txt = btn.Content?.ToString() ?? "";
                btn.Visibility = (q == "" || txt.ToLower().Contains(q)) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ── статистика / активность ────────────────────────────
        private void UpdateStats()
        {
            try
            {
                int lines = 0;
                foreach (var f in Directory.GetFiles(LogsDir(), "gui_test_*.log"))
                    lines += File.ReadAllLines(f).Length;
                LogsCountText.Text = $"logs/gui_test_*.log: {lines} записей";
                StatLogsText.Text = lines.ToString();
                LogsBadgeText.Text = lines.ToString();

                string? latest = null; var lt = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(LogsDir(), "gui_test_result_*.txt"))
                { var wt = File.GetLastWriteTime(f); if (wt > lt) { lt = wt; latest = f; } }
                bool pass = latest != null && File.ReadAllText(latest).Contains("ВЕРДИКТ: PASS");

                if (latest == null) { StatTestsText.Text = "нет данных"; StatTestsText.Foreground = B("#7d93a5"); }
                else if (pass) { StatTestsText.Text = "пройдено ✓"; StatTestsText.Foreground = B("#3fd158"); }
                else { StatTestsText.Text = "ошибка ✕"; StatTestsText.Foreground = B("#e94560"); }

                int problems = (latest != null && !pass) ? 1 : 0;
                BellBadgeText.Text = problems.ToString();
                BellBadge.Visibility = problems > 0 ? Visibility.Visible : Visibility.Collapsed;
                TestsBadgeText.Text = problems.ToString();
                TestsBadge.Visibility = problems > 0 ? Visibility.Visible : Visibility.Collapsed;

                if (!_testRunning)
                {
                    SystemStatusDot.Fill = problems == 0 ? B("#3fd158") : B("#e94560");
                    SystemStatusText.Text = problems == 0 ? "все системы OK" : "ошибка в тестах";
                }
            }
            catch { }
        }

        private void AddActivity(string icon, string text, string color)
        {
            try
            {
                var row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var ti = new TextBlock { Text = icon, Foreground = B(color), FontSize = 14 };
                var tt = new TextBlock { Text = text, Foreground = B("#eaf6ff"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tt, 1);
                var tm = new TextBlock { Text = DateTime.Now.ToString("HH:mm"), Foreground = B("#7d93a5"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tm, 2);
                row.Children.Add(ti); row.Children.Add(tt); row.Children.Add(tm);
                ActivityPanel.Children.Insert(0, row);
                while (ActivityPanel.Children.Count > 8) ActivityPanel.Children.RemoveAt(ActivityPanel.Children.Count - 1);
            }
            catch { }
        }

        // ── сохранение профиля ─────────────────────────────────
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var nick = TxtUsername.Text.Trim();
            var desc = TxtDescription.Text.Trim();
            SaveProfileToStore(nick, desc);
            _initialUsername = nick; _initialDescription = desc;
            ApplyAvatar();
            AddActivity("✓", "профиль сохранён", "#3fd158");
        }

        private static void SaveProfileToStore(string nick, string desc)
        {
            try
            {
                var t = typeof(UserProfile);
                const BindingFlags fl = BindingFlags.Public | BindingFlags.Static;
                foreach (var nm in new[] { "Save", "Set", "Update", "Store", "Write", "Apply", "Register" })
                {
                    var m = t.GetMethod(nm, fl, null, new[] { typeof(string), typeof(string) }, null);
                    if (m != null) { m.Invoke(null, new object[] { nick, desc }); return; }
                }
                var pn = t.GetProperty("Nick", fl); var pd = t.GetProperty("Description", fl);
                if (pn != null && pn.CanWrite) pn.SetValue(null, nick);
                if (pd != null && pd.CanWrite) pd.SetValue(null, desc);
                var ps = t.GetMethod("Save", fl, null, Type.EmptyTypes, null) ?? t.GetMethod("Persist", fl, null, Type.EmptyTypes, null);
                ps?.Invoke(null, null);
            }
            catch { }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            TxtUsername.Text = _initialUsername;
            TxtDescription.Text = _initialDescription;
            ApplyAvatar();
        }

        // ── тесты ──────────────────────────────────────────────
        private void SetupTests()
        {
            _tests.Clear();
            _tests.Add(new TestDef { Name = "чтение файла", Icon = "📖", Run = async () => { var f = Path.Combine(LogsDir(), "_t_read.txt"); File.WriteAllText(f, "leron"); var s = File.ReadAllText(f); File.Delete(f); await Task.CompletedTask; return s == "leron"; } });
            _tests.Add(new TestDef { Name = "запись файла", Icon = "✏️", Run = async () => { var f = Path.Combine(LogsDir(), "_t_write.txt"); File.WriteAllText(f, "ok123"); var ok = File.ReadAllText(f) == "ok123"; File.Delete(f); await Task.CompletedTask; return ok; } });
            _tests.Add(new TestDef { Name = "патч файла", Icon = "🩹", Run = async () => { var f = Path.Combine(LogsDir(), "_t_patch.txt"); File.WriteAllText(f, "a OLD b"); File.WriteAllText(f, File.ReadAllText(f).Replace("OLD", "NEW")); var ok = File.ReadAllText(f).Contains("NEW"); File.Delete(f); await Task.CompletedTask; return ok; } });
            _tests.Add(new TestDef { Name = "логи доступны", Icon = "📜", Run = async () => { var d = LogsDir(); File.WriteAllText(Path.Combine(d, "_t_log.txt"), "x"); File.Delete(Path.Combine(d, "_t_log.txt")); await Task.CompletedTask; return Directory.Exists(d); } });
            RenderTests();
        }

        private void RenderTests()
        {
            TestsContainer.Children.Clear();
            for (int i = 0; i < _tests.Count; i++)
            {
                var t = _tests[i];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var ic = new TextBlock { Text = t.Icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                var nm = new TextBlock { Text = t.Name, Foreground = B("#eaf6ff"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(nm, 1);
                t.TimeText = new TextBlock { Text = "-", Foreground = B("#7d93a5"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                Grid.SetColumn(t.TimeText, 2);
                t.Badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2, 8, 2), Background = B("#10222e"), Margin = new Thickness(0, 0, 10, 0) };
                t.BadgeText = new TextBlock { Text = "ожидание", Foreground = B("#7d93a5"), FontSize = 10.5 };
                t.Badge.Child = t.BadgeText;
                Grid.SetColumn(t.Badge, 3);
                var run = new Button { Content = "▶", Style = (Style)FindResource("BtnSecondary"), Tag = i, Padding = new Thickness(10, 3, 10, 3) };
                run.Click += RunOne_Click;
                Grid.SetColumn(run, 4);

                row.Children.Add(ic); row.Children.Add(nm); row.Children.Add(t.TimeText); row.Children.Add(t.Badge); row.Children.Add(run);
                TestsContainer.Children.Add(row);
            }
        }

        private async void RunOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not int i) return;
            var t = _tests[i];
            var sw = Stopwatch.StartNew();
            bool ok; string err = "";
            try { ok = await t.Run(); } catch (Exception ex) { ok = false; err = ex.Message; }
            sw.Stop();
            t.TimeText!.Text = $"{sw.ElapsedMilliseconds}мс";
            t.Badge!.Background = ok ? B("#0d2b1a") : B("#2b0d16");
            t.BadgeText!.Text = ok ? "OK" : "FAIL";
            t.BadgeText.Foreground = ok ? B("#3fd158") : B("#e94560");
            TestLog($"gui_test_{DateTime.Now:yyyyMMdd}.log", t.Name, "ручной запуск", ok, err);
            RefreshLogs(); UpdateStats();
        }

        private async void TestFullGui_Click(object sender, RoutedEventArgs e)
        {
            _testRunning = true;
            BtnFullTest.IsEnabled = false;
            BtnFullTest.Content = "⏳ Тестирование...";
            SystemStatusText.Text = "полный тест...";
            SystemStatusDot.Fill = B("#00d9ff");
            MainApp.GuiTestRunner.OnProgress = (text, pct) => Dispatcher.InvokeAsync(() => SystemStatusText.Text = text);
            bool pass = false;
            try
            {
                var resultPath = await Task.Run(() => MainApp.GuiTestRunner.RunFullTestAsync());
                var content = File.ReadAllText(resultPath);
                pass = content.Contains("ВЕРДИКТ: PASS");
                SystemStatusText.Text = pass ? "все системы OK" : "ошибка в тестах";
                SystemStatusDot.Fill = pass ? B("#3fd158") : B("#e94560");
                BtnFullTest.Content = pass ? "✅ Тест пройден" : "❌ Тест провален";
                AddActivity(pass ? "✓" : "✕", pass ? "запуск тестов — ок" : "запуск тестов — ошибка", pass ? "#3fd158" : "#e94560");
                try { Process.Start(new ProcessStartInfo { FileName = resultPath, UseShellExecute = true }); } catch { }
            }
            catch (Exception ex)
            {
                SystemStatusText.Text = "ошибка теста: " + ex.Message;
                SystemStatusDot.Fill = B("#e94560");
                BtnFullTest.Content = "⚡ Полный тест GUI";
                AddActivity("✕", "запуск тестов — ошибка", "#e94560");
            }
            BtnFullTest.IsEnabled = true;
            _testRunning = false;
            RefreshLogs();
            UpdateStats();
        }

        // ── логи ───────────────────────────────────────────────
        private void RefreshLogs()
        {
            try
            {
                LogsList.Children.Clear();
                var files = Directory.GetFiles(LogsDir(), "gui_test_*.log").OrderByDescending(File.GetLastWriteTime).ToList();
                if (files.Count == 0)
                {
                    LogsList.Children.Add(new TextBlock { Text = "лог-файлов пока нет", Foreground = B("#7d93a5"), FontSize = 11.5 });
                }
                else
                {
                    foreach (var line in File.ReadAllLines(files[0]).TakeLast(12).Reverse())
                    {
                        LogsList.Children.Add(new TextBlock { Text = line, Foreground = B("#9fb8c8"), FontSize = 11, Margin = new Thickness(0, 0, 0, 3), TextWrapping = TextWrapping.Wrap });
                    }
                }
                UpdateStats();
            }
            catch { }
        }

        private void OpenLogsDir_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo { FileName = LogsDir(), UseShellExecute = true }); } catch { }
        }

        // ── клавиши ────────────────────────────────────────────
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { BackRequested?.Invoke(); }
            else if (e.Key == Key.OemQuestion) { SearchBox.Focus(); e.Handled = true; }
        }
    }
}