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

// Тема «Синий HUD» (стиль Джарвиса): палитра в одном месте, фоновые
// эффекты и голографические анимации включения/выключения
// (точка → кольцо-реактор с дугой → раскрытие экрана).
// Фон — чёрный лист чертёжной сетки, редкие частицы, мягкое свечение.
public static class Theme
{
    // ── Палитра (единый источник для всех окон) ─────────────────
    public const string Bg         = "#02070c"; // почти чёрный с синим оттенком
    public const string BgDeep     = "#000000"; // чистый чёрный для гашения
    public const string Panel      = "#07141d"; // панели / «стекло»
    public const string PanelSoft  = "#0a1c28"; // более светлый слой панелей
    public const string Border     = "#12404f"; // тонкая циановая рамка
    public const string BorderDim  = "#0b2530"; // едва заметная рамка
    public const string Accent     = "#00d9ff"; // основной циан
    public const string AccentSoft = "#8fe6ff"; // светлый циан
    public const string Text       = "#eaf6ff"; // почти белый
    public const string TextDim    = "#6f96a8"; // приглушённый сине-серый
    public const string Warn       = "#ffd24a"; // предупреждения
    public const string Error      = "#e94560"; // ошибки
    public const string Success    = "#00d9ff"; // успех

    public static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static FontFamily? _font;
    public static FontFamily Font()
    {
        if (_font != null) return _font;
        try
        {
            var installed = new HashSet<string>(
                Fonts.SystemFontFamilies.Select(f => f.Source),
                StringComparer.OrdinalIgnoreCase);
            // Тонкий футуристичный: Bahnschrift (Win10+), фолбэк Segoe UI Light.
            if (installed.Contains("Bahnschrift")) _font = new FontFamily("Bahnschrift");
            else if (installed.Contains("Segoe UI Light")) _font = new FontFamily("Segoe UI Light");
        }
        catch { }
        _font ??= new FontFamily("Segoe UI");
        return _font;
    }

    // Фон: чёрный лист «чертежа» — мелкая циановая сетка (каждая 4-я линия
    // ярче), редкие всплывающие частицы, мягкое пульсирующее свечение, виньетка.
    public static Grid MakeFx(double intensity)
    {
        var grid = new Grid { IsHitTestVisible = false, Opacity = intensity };
        var rnd = new Random();

        // 1) Радиальная тёмно-синяя подсветка из центра
        var ambienceBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(14, 0, 217, 255), 0.0),
                new GradientStop(Color.FromArgb(6, 0, 120, 180), 0.55),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
            }
        };
        ambienceBrush.Freeze();
        grid.Children.Add(new Rectangle { Fill = ambienceBrush });

        // 2) Чертёжная сетка: клетки 28px, каждая 4-я линия ярче (тайл 112px)
        const double cell = 28;
        const double tile = cell * 4;
        var minorGeom = new GeometryGroup();
        var majorGeom = new GeometryGroup();
        majorGeom.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, tile)));
        majorGeom.Children.Add(new RectangleGeometry(new Rect(0, 0, tile, 1)));
        for (int i = 1; i < 4; i++)
        {
            double p = i * cell;
            minorGeom.Children.Add(new RectangleGeometry(new Rect(p, 0, 1, tile)));
            minorGeom.Children.Add(new RectangleGeometry(new Rect(0, p, tile, 1)));
        }
        var minorSolid = new SolidColorBrush(Color.FromArgb(12, 0, 217, 255));
        var majorSolid = new SolidColorBrush(Color.FromArgb(28, 0, 217, 255));
        minorSolid.Freeze();
        majorSolid.Freeze();
        var blueprintDrawing = new DrawingGroup();
        blueprintDrawing.Children.Add(new GeometryDrawing(minorSolid, null, minorGeom));
        blueprintDrawing.Children.Add(new GeometryDrawing(majorSolid, null, majorGeom));
        var blueprintBrush = new DrawingBrush(blueprintDrawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        blueprintBrush.Freeze();
        grid.Children.Add(new Rectangle { Fill = blueprintBrush });

        // 3) Редкие циановые частицы, медленно всплывающие вверх
        var canvas = new Canvas();
        grid.Children.Add(canvas);
        var parts = new List<(TranslateTransform tr, double y, double v)>();
        void Seed()
        {
            canvas.Children.Clear();
            parts.Clear();
            double W = canvas.ActualWidth, H = canvas.ActualHeight;
            if (W < 10 || H < 10) return;
            for (int i = 0; i < 20; i++)
            {
                double s = 1 + rnd.NextDouble() * 2;
                double y = rnd.NextDouble() * H;
                var tr = new TranslateTransform(0, y);
                var fill = new SolidColorBrush(Color.FromArgb((byte)(14 + rnd.Next(32)), 0, 217, 255));
                fill.Freeze();
                var el = new Ellipse { Width = s, Height = s, Fill = fill, RenderTransform = tr };
                Canvas.SetLeft(el, rnd.NextDouble() * W);
                canvas.Children.Add(el);
                parts.Add((tr, y, 5 + rnd.NextDouble() * 12));
            }
        }
        canvas.SizeChanged += (_, _) => Seed();

        // 4) Мягкая голографическая полоса света, медленно проходящая по экрану
        var bandBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0, 217, 255), 0.0),
                new GradientStop(Color.FromArgb(3, 0, 217, 255), 0.3),
                new GradientStop(Color.FromArgb(5, 0, 217, 255), 0.5),
                new GradientStop(Color.FromArgb(3, 0, 217, 255), 0.7),
                new GradientStop(Color.FromArgb(0, 0, 217, 255), 1.0)
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

        // 5) Мягкое пульсирующее циановое свечение поверх
        var glowBrush = B(Accent);
        glowBrush.Freeze();
        var glow = new Rectangle { Fill = glowBrush, Opacity = 0.018 };
        grid.Children.Add(glow);

        // 6) Виньетка к краям
        var vigBrush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55),
                new GradientStop(Color.FromArgb(70, 0, 0, 0), 1.0)
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
                double bandY = ((t * 45) % (H + 800)) - 420;
                ((TranslateTransform)band.RenderTransform).Y = bandY;
            }
            glow.Opacity = 0.016 + 0.006 * Math.Sin(t * 1.5) + 0.004 * Math.Sin(t * 3.9 + 1.2);
        }
        grid.Loaded += (_, _) => CompositionTarget.Rendering += Tick;
        grid.Unloaded += (_, _) => CompositionTarget.Rendering -= Tick;
        return grid;
    }

    // Голографическая планета с «кольцами интернета»: каркасная сфера,
    // 3 наклонных пунктирных кольца с бегущими спутниками, медленный дрейф.
    // Размер = min(W,H)*0.85, пересборка при SizeChanged; слой позади чата.
    public static Grid MakeHoloPlanet()
    {
        var grid = new Grid { IsHitTestVisible = false, Opacity = 0.6 };
        var canvas = new Canvas();
        grid.Children.Add(canvas);
        var sphere = new Grid();
        canvas.Children.Add(sphere);
        var rings = new List<Grid>();
        var ringCfg = new List<(double baseAng, double drift)>();
        var geo = new List<(double rx, double ry)>();
        var sats = new List<(Ellipse el, int ring, double phase, double speed)>();
        double cw = 0, ch = 0;
        void Build()
        {
            cw = canvas.ActualWidth; ch = canvas.ActualHeight;
            if (cw < 60 || ch < 60) return;
            double d = Math.Min(cw, ch) * 0.85;
            sphere.Children.Clear();
            sphere.Width = d; sphere.Height = d;
            Canvas.SetLeft(sphere, (cw - d) / 2);
            Canvas.SetTop(sphere, (ch - d) / 2);
            sphere.Children.Add(new Ellipse { Width = d, Height = d, Stroke = B("#12404f"), StrokeThickness = 1 });
            foreach (double k in new[] { 0.75, 0.5, 0.25 })
            {
                sphere.Children.Add(new Ellipse { Width = d * k, Height = d, Stroke = B("#0b2530"), StrokeThickness = 1 });
                sphere.Children.Add(new Ellipse { Width = d, Height = d * k, Stroke = B("#0b2530"), StrokeThickness = 1 });
            }
            foreach (var r in rings) canvas.Children.Remove(r);
            foreach (var s in sats) canvas.Children.Remove(s.el);
            rings.Clear(); ringCfg.Clear(); geo.Clear(); sats.Clear();
            double[] rw = { 1.52, 1.70, 1.88 };
            double[] rh = { 0.36, 0.50, 0.30 };
            double[] ang = { -16, -6, -26 };
            double[] drift = { 1.2, -0.7, 0.5 };
            var rnd = new Random(7);
            for (int i = 0; i < 3; i++)
            {
                var rg = new Grid
                {
                    Width = d * rw[i],
                    Height = d * rh[i],
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(ang[i])
                };
                rg.Children.Add(new Ellipse
                {
                    Stroke = B(i == 0 ? "#1f6f86" : "#0b2530"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 6, 4 }
                });
                Canvas.SetLeft(rg, (cw - rg.Width) / 2);
                Canvas.SetTop(rg, (ch - rg.Height) / 2);
                canvas.Children.Add(rg);
                rings.Add(rg);
                ringCfg.Add((ang[i], drift[i]));
                geo.Add((rg.Width / 2, rg.Height / 2));
                int n = 4 + i; // 4-6 спутников на кольцо
                for (int s = 0; s < n; s++)
                {
                    var el = new Ellipse { Width = 3, Height = 3, Fill = B("#00d9ff"), Opacity = 0.25 + rnd.NextDouble() * 0.75 };
                    canvas.Children.Add(el);
                    sats.Add((el, i, rnd.NextDouble() * Math.PI * 2, 0.25 + rnd.NextDouble() * 0.35));
                }
            }
        }
        canvas.SizeChanged += (_, _) => Build();
        var start = DateTime.Now;
        void Tick(object? s, EventArgs e)
        {
            if (cw < 60 || ch < 60) return;
            double t = (DateTime.Now - start).TotalSeconds;
            for (int i = 0; i < rings.Count; i++)
                ((RotateTransform)rings[i].RenderTransform).Angle =
                    ringCfg[i].baseAng + ringCfg[i].drift * t * 0.15 + Math.Sin(t * 0.1 + i) * 3;
            double cx = cw / 2, cy = ch / 2;
            foreach (var sat in sats)
            {
                var g = geo[sat.ring];
                double a = sat.phase + t * sat.speed;
                double lx = g.rx * Math.Cos(a);
                double ly = g.ry * Math.Sin(a);
                double ar = ((RotateTransform)rings[sat.ring].RenderTransform).Angle * Math.PI / 180;
                Canvas.SetLeft(sat.el, cx + lx * Math.Cos(ar) - ly * Math.Sin(ar) - 1.5);
                Canvas.SetTop(sat.el, cy + lx * Math.Sin(ar) + ly * Math.Cos(ar) - 1.5);
            }
            sphere.Opacity = 0.85 + 0.15 * Math.Sin(t * 0.8);
        }
        grid.Loaded += (_, _) => CompositionTarget.Rendering += Tick;
        grid.Unloaded += (_, _) => CompositionTarget.Rendering -= Tick;
        return grid;
    }
    // Анимации питания: быстрый фейд (стиль Джарвиса), без звука.
    public static class Crt
    {
        // ВКЛ: быстрый фейд из чёрного.
        public static Action PowerOn(Grid g, Action? done = null)
        {
            if (g.ActualWidth < 10 || g.ActualHeight < 10) { done?.Invoke(); return () => { }; }
            var black = new Rectangle { Fill = Brushes.Black, Opacity = 1 };
            Grid.SetRowSpan(black, 100);
            Grid.SetColumnSpan(black, 100);
            g.Children.Add(black);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                g.Children.Remove(black);
                done?.Invoke();
            }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => Finish();
            black.BeginAnimation(UIElement.OpacityProperty, fade);
            return Finish;
        }

        // ВЫКЛ: быстрый фейд в чёрный.
        public static Action PowerOff(Grid g, Action done)
        {
            if (g.ActualWidth < 10 || g.ActualHeight < 10) { done(); return () => { }; }
            var black = new Rectangle { Fill = Brushes.Black, Opacity = 0 };
            Grid.SetRowSpan(black, 100);
            Grid.SetColumnSpan(black, 100);
            g.Children.Add(black);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                g.Children.Remove(black);
                done();
            }
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => Finish();
            black.BeginAnimation(UIElement.OpacityProperty, fade);
            return Finish;
        }
    }
}