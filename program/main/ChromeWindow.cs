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
    public bool UseFadeIn = true;
    protected double FxIntensity = 0.5;
    private bool _full;

    public ChromeWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Theme.B("#04150c");
        Foreground = Theme.B("#c8ffd8");
        FontFamily = Theme.Font();
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Loaded += OnChromeLoaded;
    }

    private void OnChromeLoaded(object sender, RoutedEventArgs e)
    {
        InjectFx();
        if (StartFullscreen) GoFull();
        if (UseFadeIn) FadeIn();
        Focus();
    }

    private void InjectFx()
    {
        Grid? g = Content as Grid ?? (Content as Border)?.Child as Grid;
        if (g == null) return;
        g.Background = Theme.B("#04150c");
        for (int i = g.Children.Count - 1; i >= 0; i--)
        {
            if (g.Children[i] is Rectangle r && r.Fill is RadialGradientBrush)
                g.Children.RemoveAt(i);
        }
        var fx = Theme.MakeFx(FxIntensity);
        g.Children.Insert(0, fx);
        if (g.RowDefinitions.Count > 0)
            Grid.SetRowSpan(fx, g.RowDefinitions.Count);
    }

    // Переход без зазора: старое окно непрозрачно, пока новое не отрисовано
    // (ContentRendered) и не стало полностью непрозрачным поверх.
    protected void SwapTo(Window next)
    {
        if (next is ChromeWindow cw) cw.UseFadeIn = false;
        next.Opacity = 0;
        bool started = false;
        DispatcherTimer? fallback = null;
        Action startFade = null!;
        startFade = () =>
        {
            if (started) return;
            started = true;
            fallback?.Stop();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) => Close();
            next.BeginAnimation(OpacityProperty, fade);
        };
        fallback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        fallback.Tick += (_, _) => startFade();
        next.ContentRendered += (_, _) => startFade();
        next.Show();
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

    public void CloseClick(object sender, RoutedEventArgs e) => Close();

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