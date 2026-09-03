using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MainApp.Memory
{
    // [LERON DESIGN v3] профессиональный вид раздела памяти
    partial class MemoryPage
    {
        private static readonly DependencyProperty LeronStyledProp =
            DependencyProperty.RegisterAttached("LeronStyled", typeof(bool), typeof(MemoryPage), new PropertyMetadata(false));

        private DispatcherTimer? _designTimer;

        private static Brush BgCard() => new LinearGradientBrush(Color.FromRgb(13, 22, 38), Color.FromRgb(9, 15, 27), 90);
        private static Brush BgPanel() => new LinearGradientBrush(Color.FromRgb(8, 14, 25), Color.FromRgb(6, 10, 19), 90);
        private static Brush BrBorder() => new SolidColorBrush(Color.FromRgb(30, 58, 95));
        private static Brush TxHead() => new LinearGradientBrush(Color.FromRgb(34, 211, 238), Color.FromRgb(129, 140, 248), 0);

        private void ApplyLeronMemoryDesign()
        {
            try
            {
                this.Background = new LinearGradientBrush(Color.FromRgb(5, 8, 15), Color.FromRgb(9, 17, 28), 90);
                this.Loaded += (s, e) =>
                {
                    StylePass();
                    if (_designTimer == null)
                    {
                        _designTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                        _designTimer.Tick += (s2, e2) => StylePass();
                        _designTimer.Start();
                    }
                };
            }
            catch { }
        }

        private void StylePass()
        {
            try { StyleTree(this, null); } catch { }
        }

        private void StyleTree(DependencyObject el, DependencyObject parent)
        {
            try { StyleOne(el, parent); } catch { }
            int n = VisualTreeHelper.GetChildrenCount(el);
            for (int i = 0; i < n; i++)
                StyleTree(VisualTreeHelper.GetChild(el, i), el);
        }

        private bool Done(DependencyObject el) => (bool)el.GetValue(LeronStyledProp);
        private void Mark(DependencyObject el) => el.SetValue(LeronStyledProp, true);

        private void StyleOne(DependencyObject el, DependencyObject parent)
        {
            if (Done(el)) return;

            // ── ГРАФ СВЯЗЕЙ: сетка + радиальный фон ──
            if (el is Canvas canvas)
            {
                if (!(canvas.Background is RadialGradientBrush))
                    canvas.Background = new RadialGradientBrush(Color.FromRgb(10, 20, 36), Color.FromRgb(5, 9, 17));
                EnsureGrid(canvas);
                return; // не помечаем: граф перерисовывается
            }

            // ── точки графа: свечение ──
            if (el is Ellipse ep)
            {
                var sc = ep.Fill as SolidColorBrush;
                var col = sc != null ? sc.Color : Colors.Cyan;
                if (ep.Width > 0 && ep.Width < 12) { ep.Width = 10; ep.Height = 10; }
                ep.Effect = new DropShadowEffect { Color = col, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };
                Mark(el);
                return;
            }

            // ── кнопки: пилюли с градиентом ──
            if (el is Button btn)
            {
                string txt = btn.Content?.ToString() ?? "";
                if (txt.Contains("забыть"))
                {
                    btn.Background = new LinearGradientBrush(Color.FromRgb(64, 16, 24), Color.FromRgb(42, 10, 16), 90);
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(255, 99, 116));
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(127, 29, 42));
                }
                else
                {
                    btn.Background = new LinearGradientBrush(Color.FromRgb(15, 42, 60), Color.FromRgb(10, 28, 44), 90);
                    btn.Foreground = new SolidColorBrush(Color.FromRgb(103, 232, 249));
                    btn.BorderBrush = new SolidColorBrush(Color.FromRgb(21, 94, 117));
                }
                btn.FontWeight = FontWeights.SemiBold;
                btn.Padding = new Thickness(14, 6, 14, 6);
                Mark(el);
                return;
            }

            // ── карточки и панели ──
            if (el is Border b)
            {
                if (b.Height > 0 && b.Height <= 3) { Mark(el); return; }

                bool isCard = parent is StackPanel && (b.Child is StackPanel || b.Child is Grid);

                b.Background = isCard ? BgCard() : BgPanel();
                b.BorderBrush = BrBorder();
                if (b.BorderThickness.Top < 1) b.BorderThickness = new Thickness(1);
                b.CornerRadius = new CornerRadius(isCard ? 12 : 10);

                if (isCard)
                {
                    b.Margin = new Thickness(0, 0, 0, 10);
                    if (b.Padding == new Thickness(0)) b.Padding = new Thickness(14, 10, 14, 10);
                    b.Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 14,
                        ShadowDepth = 3,
                        Direction = 270,
                        Opacity = 0.45
                    };
                }
                Mark(el);
                return;
            }

            if (el is TextBox tb)
            {
                tb.Background = new SolidColorBrush(Color.FromRgb(8, 15, 27));
                tb.Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                tb.BorderBrush = BrBorder();
                tb.CaretBrush = new SolidColorBrush(Color.FromRgb(34, 211, 238));
                Mark(el);
                return;
            }

            // ── заголовки: неоновый градиентный текст ──
            if (el is TextBlock t)
            {
                string txt = (t.Text ?? "").Trim();
                if (txt == "ДОЛГОСРОЧНАЯ ПАМЯТЬ" || txt == "КРАТКОСРОЧНАЯ ПАМЯТЬ" ||
                    txt == "ГРАФ СВЯЗЕЙ" || txt == "КАТЕГОРИИ")
                {
                    t.Foreground = TxHead();
                    t.FontWeight = FontWeights.Bold;
                    t.FontSize = 13;
                    Mark(el);
                    return;
                }
                if (txt.StartsWith("ПАМЯТЬ"))
                {
                    t.Foreground = TxHead();
                    t.FontWeight = FontWeights.ExtraBold;
                    t.FontSize = 20;
                    Mark(el);
                    return;
                }
            }
        }

        private void EnsureGrid(Canvas canvas)
        {
            try
            {
                double w = canvas.ActualWidth;
                double h = canvas.ActualHeight;
                if (w < 60 || h < 60) return;

                bool has = canvas.Children.Count > 0 &&
                           canvas.Children[0] is FrameworkElement fe &&
                           Equals(fe.Tag, "leron_grid");
                if (has) return;

                for (int i = canvas.Children.Count - 1; i >= 0; i--)
                {
                    if (canvas.Children[i] is FrameworkElement f && Equals(f.Tag, "leron_grid"))
                        canvas.Children.RemoveAt(i);
                }

                var brush = new SolidColorBrush(Color.FromRgb(14, 32, 52));
                brush.Opacity = 0.45;

                for (double x = 40; x < w; x += 40)
                    canvas.Children.Insert(0, new Line { X1 = x, Y1 = 0, X2 = x, Y2 = h, Stroke = brush, StrokeThickness = 1, Tag = "leron_grid" });

                for (double y = 40; y < h; y += 40)
                    canvas.Children.Insert(0, new Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = brush, StrokeThickness = 1, Tag = "leron_grid" });
            }
            catch { }
        }
    }
}