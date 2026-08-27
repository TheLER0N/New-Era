using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace MainApp;
// Арт хаба: ядро HUB 00, плоская карточка-нода, точка-слот.
public static class HubArt
{
public static SolidColorBrush B(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
public class ProjectView
{
public string Name = "";
public string PathLine = "";
public string StatsLine = "";
public string TimeLine = "";
public string ToolTip = "";
}
public static FrameworkElement HubCore()
{
var g = new Grid { Width = 96, Height = 96 };
g.Children.Add(new Ellipse { Width = 96, Height = 96, Stroke = B("#12404f"), StrokeThickness = 1, Fill = B("#07141d") });
g.Children.Add(new Ellipse { Width = 72, Height = 72, Stroke = B("#00d9ff"), StrokeThickness = 1, Fill = Brushes.Transparent });
g.Children.Add(new TextBlock { Text = "HUB 00", Foreground = B("#8fe6ff"), FontFamily = new FontFamily("Consolas"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
return g;
}
// Точка-слот: пустая — пунктир с «+», с проектами — сплошная с «×N», имя снизу.
public static Grid PointDot(string name, int count, bool empty, bool selected)
{
var g = new Grid { Width = 40, Height = 64, Cursor = Cursors.Hand };
var inner = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Top };
var ring = new Ellipse
{
Width = 40, Height = 40,
Stroke = B(selected ? "#00d9ff" : empty ? "#6f96a8" : "#1f6f86"),
StrokeThickness = selected ? 2 : 1.5,
Fill = B("#07141d")
};
if (empty) ring.StrokeDashArray = new DoubleCollection { 3, 3 };
inner.Children.Add(ring);
if (selected) inner.Children.Add(new Ellipse { Width = 52, Height = 52, Stroke = B("#00d9ff"), StrokeThickness = 1, Opacity = 0.4 });
inner.Children.Add(new TextBlock
{
Text = empty ? "+" : "×" + count,
Foreground = B(empty ? "#6f96a8" : "#8fe6ff"),
FontSize = empty ? 16 : 11,
FontFamily = new FontFamily("Consolas"),
HorizontalAlignment = HorizontalAlignment.Center,
VerticalAlignment = VerticalAlignment.Center
});
var nameTb = new TextBlock
{
Text = name,
Foreground = B(selected ? "#8fe6ff" : "#6f96a8"),
FontSize = 10,
FontFamily = new FontFamily("Consolas"),
HorizontalAlignment = HorizontalAlignment.Center,
VerticalAlignment = VerticalAlignment.Top,
Margin = new Thickness(-25, 44, -25, 0),
TextTrimming = TextTrimming.CharacterEllipsis
};
g.Children.Add(inner);
g.Children.Add(nameTb);
return g;
}
public static Border FolderCard(ProjectView v, bool selected, bool multi)
{
var card = new Border
{
Width = 240, Height = 92,
CornerRadius = new CornerRadius(8),
Background = B("#07141d"),
BorderBrush = B(selected ? "#00d9ff" : "#12404f"),
BorderThickness = new Thickness(selected ? 1.5 : 1),
Cursor = Cursors.Hand
};
var sp = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
sp.Children.Add(new TextBlock { Text = "📁 " + v.Name, Foreground = B("#00d9ff"), FontWeight = FontWeights.Bold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
sp.Children.Add(new TextBlock { Text = v.PathLine, Foreground = B("#6f96a8"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
sp.Children.Add(new TextBlock { Text = v.StatsLine, Foreground = B("#eaf6ff"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
sp.Children.Add(new TextBlock { Text = v.TimeLine, Foreground = B("#6f96a8"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
if (multi)
{
var gg = new Grid();
gg.Children.Add(sp);
gg.Children.Add(new Rectangle { Stroke = B("#00d9ff"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, Margin = new Thickness(-3), IsHitTestVisible = false });
card.Child = gg;
}
else card.Child = sp;
if (!string.IsNullOrEmpty(v.ToolTip)) card.ToolTip = v.ToolTip;
return card;
}
}