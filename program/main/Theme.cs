using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MainApp;

public static class Theme
{
    public static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

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

    public static Grid MakeFx(double intensity)
    {
        var grid = new Grid { IsHitTestVisible = false, Opacity = intensity };
        var rnd = new Random();
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

        var glowBrush = B("#00ff88");
        glowBrush.Freeze();
        var glow = new Rectangle { Fill = glowBrush, Opacity = 0.028 };
        grid.Children.Add(glow);

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

    public static class Crt
    {
        public static Action PowerOn(Grid g, Action? done = null)
        {
            Action? skipAction = null;
            bool finished = false;
            void Run()
            {
                double W = g.ActualWidth, H = g.ActualHeight;
                if (W < 10 || H < 10) { done?.Invoke(); return; }

                var black = new Rectangle { Fill = Brushes.Black, Opacity = 1 };
                Grid.SetRowSpan(black, 100);
                var beamBrush = new SolidColorBrush(Colors.White);
                var beam = new Rectangle
                {
                    Fill = beamBrush, Width = 4, Height = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                    Effect = new BlurEffect { Radius = 8 }
                };
                Grid.SetRowSpan(beam, 100);

                g.Children.Add(black);
                g.Children.Add(beam);

                skipAction = () =>
                {
                    if (finished) return;
                    finished = true;
                    var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                    fade.Completed += (_, _) => { g.Children.Remove(black); g.Children.Remove(beam); done?.Invoke(); };
                    black.BeginAnimation(UIElement.OpacityProperty, fade);
                    beam.BeginAnimation(UIElement.OpacityProperty, fade);
                };

                try { System.Media.SystemSounds.Beep.Play(); } catch { }

                var dotFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                dotFadeIn.Completed += (_, _) =>
                {
                    if (finished) return;
                    var lineExpand = new DoubleAnimation(4, W, TimeSpan.FromMilliseconds(250))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    lineExpand.Completed += (_, _) =>
                    {
                        if (finished) return;
                        var hExpand = new DoubleAnimation(4, H, TimeSpan.FromMilliseconds(450))
                            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                        var colorAnim = new ColorAnimation(Colors.White, (Color)ColorConverter.ConvertFromString("#00ff88"), TimeSpan.FromMilliseconds(450));

                        hExpand.Completed += (_, _) =>
                        {
                            if (finished) return;
                            var revealFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                            revealFade.Completed += (_, _) =>
                            {
                                if (finished) return;
                                finished = true;
                                g.Children.Remove(black);
                                g.Children.Remove(beam);
                                done?.Invoke();
                            };
                            black.BeginAnimation(UIElement.OpacityProperty, revealFade);
                            beam.BeginAnimation(UIElement.OpacityProperty, revealFade);
                        };
                        beam.BeginAnimation(FrameworkElement.HeightProperty, hExpand);
                        beamBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                    };
                    beam.BeginAnimation(FrameworkElement.WidthProperty, lineExpand);
                };
                beam.BeginAnimation(UIElement.OpacityProperty, dotFadeIn);
            }

            if (g.ActualHeight >= 10) Run();
            else
            {
                SizeChangedEventHandler? on = null;
                on = (_, _) => { if (g.ActualHeight < 10) return; g.SizeChanged -= on; Run(); };
                g.SizeChanged += on;
            }
            return () => skipAction?.Invoke();
        }

        public static Action PowerOff(Grid g, Action done)
        {
            double W = g.ActualWidth, H = g.ActualHeight;
            if (W < 10 || H < 10) { done(); return () => { }; }

            var top = new Rectangle { Fill = Brushes.Black, Height = 0, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetRowSpan(top, 100);
            var bot = new Rectangle { Fill = Brushes.Black, Height = 0, VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRowSpan(bot, 100);
            var lineBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00ff88"));
            var line = new Rectangle
            {
                Fill = lineBrush, Height = 2, Width = W,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                Effect = new BlurEffect { Radius = 4 }
            };
            Grid.SetRowSpan(line, 100);
            var dotBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00ff88"));
            var dot = new Ellipse
            {
                Fill = dotBrush, Width = 6, Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0,
                Effect = new BlurEffect { Radius = 6 }
            };
            Grid.SetRowSpan(dot, 100);

            g.Children.Add(top);
            g.Children.Add(bot);
            g.Children.Add(line);
            g.Children.Add(dot);

            bool finished = false;
            Action skipAction = () =>
            {
                if (finished) return;
                finished = true;
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fade.Completed += (_, _) =>
                {
                    g.Children.Remove(top);
                    g.Children.Remove(bot);
                    g.Children.Remove(line);
                    g.Children.Remove(dot);
                    done();
                };
                line.BeginAnimation(UIElement.OpacityProperty, fade);
                dot.BeginAnimation(UIElement.OpacityProperty, fade);
            };

            try { System.Media.SystemSounds.Beep.Play(); } catch { }

            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var topAnim = new DoubleAnimation(0, H / 2 - 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = easeIn };
            var botAnim = new DoubleAnimation(0, H / 2 - 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = easeIn };

            topAnim.Completed += (_, _) =>
            {
                if (finished) return;
                line.Opacity = 1;
                var lineShrink = new DoubleAnimation(W, 6, TimeSpan.FromMilliseconds(250)) { EasingFunction = easeIn };
                lineShrink.Completed += (_, _) =>
                {
                    if (finished) return;
                    line.Opacity = 0;
                    dot.Opacity = 1;
                    var dotFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                    dotFade.Completed += (_, _) =>
                    {
                        if (finished) return;
                        finished = true;
                        g.Children.Remove(top);
                        g.Children.Remove(bot);
                        g.Children.Remove(line);
                        g.Children.Remove(dot);
                        done();
                    };
                    dot.BeginAnimation(UIElement.OpacityProperty, dotFade);
                };
                line.BeginAnimation(FrameworkElement.WidthProperty, lineShrink);
            };
            top.BeginAnimation(FrameworkElement.HeightProperty, topAnim);
            bot.BeginAnimation(FrameworkElement.HeightProperty, botAnim);

            return () => skipAction();
        }
    }
}