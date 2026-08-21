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
    public bool UseFadeIn = false;
    protected double FxIntensity = 0.0;
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
        if (StartFullscreen) GoFull();
        if (UseFadeIn) FadeIn();
        Focus();
    }

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