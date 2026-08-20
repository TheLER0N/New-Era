using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MainApp;

public static class Theme
{
    public static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    // Кэш: перебор системных шрифтов — дорогая операция, делаем её один раз.
    private static FontFamily? _font;
    public static FontFamily Font()
    {
        if (_font != null) return _font;
        try
        {
            if (Fonts.SystemFontFamilies.Any(f => f.Source.Equals("JetBrains Mono", StringComparison.OrdinalIgnoreCase)))
                _font = new FontFamily("JetBrains Mono");
        }
        catch { }
        _font ??= new FontFamily("Consolas");
        return _font;
    }

    // Единый слой "старого монитора". Минимум полноэкранных проходов.
    public static Grid MakeFx(double intensity)
    {
        var grid = new Grid { IsHitTestVisible = false, Opacity = intensity };
        var rnd = new Random();

        // 1. Мягкое широкое свечение.
        var ambienceBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(12, 0, 255, 136), 0.0),
                new GradientStop(Color.FromArgb(5, 0, 255, 136), 0.55),
                new GradientStop(Color.FromArgb(0, 0, 255, 136), 1.0)
            }
        };
        ambienceBrush.Freeze();
        grid.Children.Add(new Rectangle { Fill = ambienceBrush });

        // 2. Сканлайны + сетка ОДНИМ слоем. Клетки 64×64 — квадраты.
        var scanGeom = new GeometryGroup();
        for (int y = 0; y < 192; y += 3)
            scanGeom.Children.Add(new RectangleGeometry(new Rect(0, y, 64, 1)));
        var gridGeom = new GeometryGroup();
        gridGeom.Children.Add(new RectangleGeometry(new Rect(63, 0, 1, 192)));
        for (int y = 63; y < 192; y += 64)
            gridGeom.Children.Add(new RectangleGeometry(new Rect(0, y, 64, 1)));
        var scanSolid = new SolidColorBrush(Color.FromArgb(22, 0, 0, 0));
        scanSolid.Freeze();
        var gridSolid = new SolidColorBrush(Color.FromArgb(18, 0, 255, 136));
        gridSolid.Freeze();
        var crtDrawing = new DrawingGroup();
        crtDrawing.Children.Add(new GeometryDrawing(scanSolid, null, scanGeom));
        crtDrawing.Children.Add(new GeometryDrawing(gridSolid, null, gridGeom));
        var crtBrush = new DrawingBrush(crtDrawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 64, 192),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        crtBrush.Freeze();
        grid.Children.Add(new Rectangle { Fill = crtBrush });

        // 3. Пыль — 28 частиц, движение через RenderTransform.
        var canvas = new Canvas();
        grid.Children.Add(canvas);
        var parts = new List<(TranslateTransform tr, double y, double v)>();
        void Seed()
        {
            canvas.Children.Clear();
            parts.Clear();
            double W = canvas.ActualWidth, H = canvas.ActualHeight;
            if (W < 10 || H < 10) return;
            for (int i = 0; i < 28; i++)
            {
                double s = 1 + rnd.NextDouble() * 2;
                double y = rnd.NextDouble() * H;
                var tr = new TranslateTransform(0, y);
                var fill = new SolidColorBrush(Color.FromArgb((byte)(16 + rnd.Next(40)), 0, 255, 136));
                fill.Freeze();
                var el = new Ellipse { Width = s, Height = s, Fill = fill, RenderTransform = tr };
                Canvas.SetLeft(el, rnd.NextDouble() * W);
                canvas.Children.Add(el);
                parts.Add((tr, y, 6 + rnd.NextDouble() * 14));
            }
        }
        canvas.SizeChanged += (_, _) => Seed();

        // 4. Регенерация — широкий медленный дрейф света.
        var bandBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0, 255, 136), 0.0),
                new GradientStop(Color.FromArgb(3, 0, 255, 136), 0.3),
                new GradientStop(Color.FromArgb(5, 0, 255, 136), 0.5),
                new GradientStop(Color.FromArgb(3, 0, 255, 136), 0.7),
                new GradientStop(Color.FromArgb(0, 0, 255, 136), 1.0)
            }
        };
        bandBrush.Freeze();
        var band = new Rectangle
        {
            Height = 360,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = bandBrush,
            RenderTransform = new TranslateTransform(0, -420)
        };
        grid.Children.Add(band);

        // 5. Равномерное дыхание люминофора.
        var glowBrush = B("#00ff88");
        glowBrush.Freeze();
        var glow = new Rectangle { Fill = glowBrush, Opacity = 0.028 };
        grid.Children.Add(glow);

        // 6. Виньетка.
        var vigBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55),
                new GradientStop(Color.FromArgb(55, 0, 0, 0), 1.0)
            }
        };
        vigBrush.Freeze();
        grid.Children.Add(new Rectangle { Fill = vigBrush });

        var last = DateTime.Now;
        var start = DateTime.Now;
        void Tick(object? s, EventArgs e)
        {
            var now = DateTime.Now;
            double dt = (now - last).TotalSeconds;
            last = now;
            double t = (now - start).TotalSeconds;
            double H = canvas.ActualHeight;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.y -= p.v * dt;
                if (p.y < -4) p.y = H + 4;
                p.tr.Y = p.y;
                parts[i] = p;
            }
            if (H > 10)
            {
                double bandY = ((t * 55) % (H + 800)) - 420;
                ((TranslateTransform)band.RenderTransform).Y = bandY;
            }
            glow.Opacity = 0.026 + 0.008 * Math.Sin(t * 1.7) + 0.005 * Math.Sin(t * 4.3 + 1.2);
        }
        grid.Loaded += (_, _) => CompositionTarget.Rendering += Tick;
        grid.Unloaded += (_, _) => CompositionTarget.Rendering -= Tick;
        return grid;
    }

    // "Включение" ЭЛТ: чёрный экран → вспышка линии → раскрытие из центра.
    public static void PowerOn(Window w)
    {
        void TryRun()
        {
            var g = w.Content as Grid ?? (w.Content as Border)?.Child as Grid;
            if (g == null) return;
            if (g.ActualHeight >= 10) RunPowerOn(g, g.ActualHeight);
            else
            {
                SizeChangedEventHandler? onSize = null;
                onSize = (_, _) =>
                {
                    if (g.ActualHeight < 10) return;
                    g.SizeChanged -= onSize;
                    RunPowerOn(g, g.ActualHeight);
                };
                g.SizeChanged += onSize;
            }
        }
        if (w.IsLoaded) TryRun();
        else w.Loaded += (_, _) => TryRun();
    }

    private static void RunPowerOn(Grid g, double H)
    {
        var overlay = new Grid { IsHitTestVisible = false };
        var black = B("#000000"); black.Freeze();
        var top = new Rectangle { Fill = black, Height = H, VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(0, 0) };
        var bot = new Rectangle { Fill = black, Height = H, VerticalAlignment = VerticalAlignment.Bottom, RenderTransform = new TranslateTransform(0, 0) };
        var lineFill = B("#d8ffe9"); lineFill.Freeze();
        var line = new Rectangle { Fill = lineFill, Height = 2.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
        overlay.Children.Add(top);
        overlay.Children.Add(bot);
        overlay.Children.Add(line);
        g.Children.Add(overlay);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        var aTop = new DoubleAnimation(0, -H, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(150), EasingFunction = ease };
        var aBot = new DoubleAnimation(0, H, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(150), EasingFunction = ease };
        aTop.Completed += (_, _) =>
        {
            line.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280)));
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(280) };
            fade.Completed += (_, _) => g.Children.Remove(overlay);
            overlay.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        ((TranslateTransform)top.RenderTransform).BeginAnimation(TranslateTransform.YProperty, aTop);
        ((TranslateTransform)bot.RenderTransform).BeginAnimation(TranslateTransform.YProperty, aBot);
    }

    // "Выключение" ЭЛТ: шторки схлопываются к центру, луч сжимается в точку.
    public static void PowerOff(Window w, Action onDone)
    {
        var g = w.Content as Grid ?? (w.Content as Border)?.Child as Grid;
        if (g == null) { onDone(); return; }
        double H = g.ActualHeight, W = g.ActualWidth;
        if (H < 10 || W < 10) { onDone(); return; }
        var overlay = new Grid { IsHitTestVisible = false };
        var black = B("#000000"); black.Freeze();
        var top = new Rectangle { Fill = black, Height = 0, VerticalAlignment = VerticalAlignment.Top };
        var bot = new Rectangle { Fill = black, Height = 0, VerticalAlignment = VerticalAlignment.Bottom };
        var lineFill = B("#d8ffe9"); lineFill.Freeze();
        var line = new Rectangle
        {
            Fill = lineFill,
            Height = 2.5,
            Width = W,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        var dot = new Ellipse
        {
            Fill = lineFill,
            Width = 6,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0
        };
        overlay.Children.Add(top);
        overlay.Children.Add(bot);
        overlay.Children.Add(line);
        overlay.Children.Add(dot);
        g.Children.Add(overlay);
        var easeIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var aTop = new DoubleAnimation(0, H / 2, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeIn };
        var aBot = new DoubleAnimation(0, H / 2, TimeSpan.FromMilliseconds(320)) { EasingFunction = easeIn };
        aTop.Completed += (_, _) =>
        {
            line.Opacity = 1;
            var aLine = new DoubleAnimation(W, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            aLine.Completed += (_, _) =>
            {
                dot.Opacity = 1;
                var aDot = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
                aDot.Completed += (_, _) =>
                {
                    g.Children.Remove(overlay);
                    onDone();
                };
                dot.BeginAnimation(UIElement.OpacityProperty, aDot);
            };
            line.BeginAnimation(FrameworkElement.WidthProperty, aLine);
        };
        top.BeginAnimation(FrameworkElement.HeightProperty, aTop);
        bot.BeginAnimation(FrameworkElement.HeightProperty, aBot);
    }
}