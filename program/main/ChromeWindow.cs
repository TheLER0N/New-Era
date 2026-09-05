using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MainApp;

public class ChromeWindow : Window
{
    public bool StartFullscreen = true;
    // Сплэш выключает: его вход даёт PowerOn, а не дорогой фейд всего окна.
    public bool UseFadeIn = false;
    protected double FxIntensity = 0.0;
    private bool _full;
    private bool _poweringDown;

    public ChromeWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Theme.B("#02070c");
        Foreground = Theme.B("#eaf6ff");
        FontFamily = Theme.Font();
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Loaded += OnChromeLoaded;
    }

    private void OnChromeLoaded(object sender, RoutedEventArgs e)
    {
        if (StartFullscreen) GoFull();
        if (UseFadeIn) FadeIn();
        Focus();
    }

    // Переход без зазора: старое окно непрозрачно, пока новое не отрисовано
    // и не стало полностью непрозрачным поверх.
    protected void SwapTo(Window next)
    {
        if (next is ChromeWindow cw) cw.UseFadeIn = false;
        next.Opacity = 0;
        next.Show();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) => Close();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fadeOut);
        next.BeginAnimation(OpacityProperty, fadeIn);
    }

    // Кнопка ✕: экран плавно гаснет в чёрный и через 2 секунды приложение
    // гарантированно завершается. История и конфиг пишутся синхронно при
    // каждом действии, поэтому к моменту гашения сохранять уже нечего —
    // эти 2 секунды идут на спокойное завершение, а OnExit в App.xaml.cs
    // добивает фоновые потоки gateway/WebView2 через Environment.Exit.
    public void CloseClick(object sender, RoutedEventArgs e) => PowerDown();

    private void PowerDown()
    {
        if (_poweringDown) return;
        _poweringDown = true;

        // блокируем ввод на время гашения
        IsEnabled = false;

        var root = Content as Grid ?? (Content as Border)?.Child as Grid;
            if (root == null) { try { Application.Current?.Shutdown(); } catch { System.Environment.Exit(0); } return; }
        if (root != null)
        {
            var black = new Rectangle { Fill = Brushes.Black, Opacity = 0 };
            Grid.SetRowSpan(black, 100);
            Grid.SetColumnSpan(black, 100);
            root.Children.Add(black);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            black.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ровно 2 секунды от нажатия — и выход
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            Application.Current?.Shutdown();
        };
        t.Start();
    }

    // Штатный OnLastWindowClose не срабатывает: скрытое окно браузерной панели
    // считается «открытым» и держит процесс. Гасим сами, как только
    // не осталось ни одного ВИДИМОГО окна.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        var app = Application.Current;
        if (app == null) return;
        foreach (Window w in app.Windows)
        {
            if (w.IsVisible) return;
        }
        app.Shutdown();
    }

    public void GoFull()
    {
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        _full = true;
    }

    public void ToggleSize(object sender, RoutedEventArgs e)
    {
        if (_full)
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + 120;
            Top = wa.Top + 90;
            Width = Math.Max(640, wa.Width - 240);
            Height = Math.Max(480, wa.Height - 180);
            _full = false;
        }
        else GoFull();
    }

    public void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (e.ClickCount == 2) { ToggleSize(sender, e); return; }
        if (!_full) DragMove();
    }

    public void FadeIn()
    {
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }
}