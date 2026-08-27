using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace MainApp;
public partial class ProjectHubWindow
{
private enum Drag { None, Pan, Node, Hub, Zone, ZoneResize, Marquee, MarqueeZone, Point }
private Drag _drag = Drag.None;
private Point _lastScreen, _downScreen, _startWorld, _marqueeStart;
private bool _moved;
private int _dragIndex = -1;
private double _dragBaseX, _dragBaseY, _startW, _startH;
private Rectangle? _marqueeRect;
private bool _zoneMode;
private VisualBrush? _fineBrush, _boldBrush;
private List<(NodeUi N, double X, double Y)> _pointDragNodes = new();
private List<(NodeUi N, double X, double Y)> _zoneDragNodes = new();
private List<(PointUi Pu, double X, double Y)> _zoneDragPoints = new();
private void InitCanvas()
{
_fineBrush = MakeGridBrush(28, "#0b2530", 1.0);
_boldBrush = MakeGridBrush(112, "#12404f", 0.5);
GridFine.Fill = _fineBrush;
GridBold.Fill = _boldBrush;
TraceText.Text = $"TRACE 0x{Environment.TickCount & 0xFFFF:X4}";
WorldCanvas.MouseLeftButtonDown += OnCanvasDown;
WorldCanvas.MouseMove += OnCanvasMove;
WorldCanvas.MouseLeftButtonUp += OnCanvasUp;
WorldCanvas.MouseWheel += OnWheel;
}
private static VisualBrush MakeGridBrush(double size, string color, double opacity)
{
var c = new Canvas { Width = size, Height = size };
c.Children.Add(new Line { X1 = 0, Y1 = size - 0.5, X2 = size, Y2 = size - 0.5, Stroke = HubArt.B(color), StrokeThickness = 1, Opacity = opacity });
c.Children.Add(new Line { X1 = size - 0.5, Y1 = 0, X2 = size - 0.5, Y2 = size, Stroke = HubArt.B(color), StrokeThickness = 1, Opacity = opacity });
return new VisualBrush(c)
{
TileMode = TileMode.Tile,
Viewport = new Rect(0, 0, size, size),
ViewportUnits = BrushMappingMode.Absolute,
Viewbox = new Rect(0, 0, size, size),
ViewboxUnits = BrushMappingMode.Absolute
};
}
private void ApplyCamera()
{
WorldCanvas.RenderTransform = _cam.Transform;
var m = new MatrixTransform(_cam.Zoom, 0, 0, _cam.Zoom, _cam.PanX, _cam.PanY);
if (_fineBrush != null) _fineBrush.Transform = m;
if (_boldBrush != null) _boldBrush.Transform = m;
GridFine.Opacity = _cam.Zoom < 0.45 ? 0 : 1;
ZoomText.Text = (int)Math.Round(_cam.Zoom * 100) + "%";
}
private Point ViewCenter => new(OverlayCanvas.ActualWidth / 2, OverlayCanvas.ActualHeight / 2);
private void SetNodePos(NodeUi n) => _pos[n.P.Path.ToLowerInvariant()] = (n.X, n.Y);
private static bool CenterIn(NodeUi n, ZoneData z)
{
double cx = n.X + 120, cy = n.Y + 46;
return cx > z.X && cx < z.X + z.W && cy > z.Y && cy < z.Y + z.H;
}
private void OnCanvasDown(object sender, MouseButtonEventArgs e)
{
ClosePointMenu();
if (_moveMode) { ExitMoveMode(); Render(); }
WorldCanvas.CaptureMouse();
_lastScreen = _downScreen = e.GetPosition(OverlayCanvas);
_startWorld = e.GetPosition(WorldCanvas);
_moved = false;
if (_zoneMode) { _drag = Drag.MarqueeZone; _marqueeStart = _lastScreen; BeginMarquee(); }
else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { _drag = Drag.Marquee; _marqueeStart = _lastScreen; BeginMarquee(); }
else _drag = Drag.Pan;
e.Handled = true;
}
private void OnNodeDown(int idx, MouseButtonEventArgs e)
{
if (e.ClickCount == 2) { OpenProject(_projects[idx]); return; }
ClosePointMenu();
WorldCanvas.CaptureMouse();
_drag = Drag.Node;
_dragIndex = idx;
_startWorld = e.GetPosition(WorldCanvas);
_lastScreen = _downScreen = e.GetPosition(OverlayCanvas);
_moved = false;
_dragBaseX = _nodes[idx].X; _dragBaseY = _nodes[idx].Y;
e.Handled = true;
}
private void OnHubDown(object sender, MouseButtonEventArgs e)
{
WorldCanvas.CaptureMouse();
_drag = Drag.Hub;
_startWorld = e.GetPosition(WorldCanvas);
_lastScreen = _downScreen = e.GetPosition(OverlayCanvas);
_moved = false;
_dragBaseX = _hubX; _dragBaseY = _hubY;
e.Handled = true;
}
private void OnPointDown(int idx, MouseButtonEventArgs e)
{
var pu = _points[idx];
var pd = pu.Data;
ClosePointMenu();
if (_moveMode)
{
if (_moveCard >= 0) Attach(_moveCard, pd.Id);
ExitMoveMode();
e.Handled = true;
return;
}
if (PointCount(pd.Id) == 0) { ShowPointMenu(pd); e.Handled = true; return; }
WorldCanvas.CaptureMouse();
_drag = Drag.Point;
_dragIndex = idx;
_startWorld = e.GetPosition(WorldCanvas);
_lastScreen = _downScreen = e.GetPosition(OverlayCanvas);
_moved = false;
_dragBaseX = pd.X; _dragBaseY = pd.Y;
_pointDragNodes = _nodes.Where(n => n.Pt == pd).Select(n => (n, n.X, n.Y)).ToList();
_selectedPointId = pd.Id;
_selected = -1; _linkSel = -1;
e.Handled = true;
}
private void OnLinkDown(int idx)
{
_linkSel = idx;
_selected = -1; _selectedPointId = null;
Render();
}
private void OnZoneDown(int idx, MouseButtonEventArgs e)
{
WorldCanvas.CaptureMouse();
_drag = Drag.Zone;
_dragIndex = idx;
_startWorld = e.GetPosition(WorldCanvas);
_lastScreen = _downScreen = e.GetPosition(OverlayCanvas);
_moved = false;
_dragBaseX = _zones[idx].Data.X; _dragBaseY = _zones[idx].Data.Y;
var z = _zones[idx].Data;
var freeIn = _nodes.Where(n => n.Pt == null && CenterIn(n, z));
var ptsIn = _points.Where(pu2 => pu2.Data.X > z.X && pu2.Data.X < z.X + z.W && pu2.Data.Y > z.Y && pu2.Data.Y < z.Y + z.H).ToList();
var ptCards = ptsIn.SelectMany(pu2 => _nodes.Where(n => n.Pt == pu2.Data));
_zoneDragNodes = freeIn.Concat(ptCards).Select(n => (n, n.X, n.Y)).ToList();
_zoneDragPoints = ptsIn.Select(pu2 => (pu2, pu2.Data.X, pu2.Data.Y)).ToList();
e.Handled = true;
}
private void OnZoneResizeDown(int idx, MouseButtonEventArgs e)
{
WorldCanvas.CaptureMouse();
_drag = Drag.ZoneResize;
_dragIndex = idx;
_startWorld = e.GetPosition(WorldCanvas);
_lastScreen = _downScreen = e.GetPosition(OverlayCanvas);
_moved = false;
_startW = _zones[idx].Data.W; _startH = _zones[idx].Data.H;
e.Handled = true;
}
private void OnCanvasMove(object sender, MouseEventArgs e)
{
var w = e.GetPosition(WorldCanvas);
XyText.Text = $"X:{(int)w.X:000} Y:{(int)w.Y:000}";
if (_drag == Drag.None) return;
var s = e.GetPosition(OverlayCanvas);
if ((s - _downScreen).Length > 4) _moved = true;
switch (_drag)
{
case Drag.Pan:
_cam.PanBy(s.X - _lastScreen.X, s.Y - _lastScreen.Y);
ApplyCamera();
break;
case Drag.Node:
var n = _nodes[_dragIndex];
n.X = _dragBaseX + (w.X - _startWorld.X);
n.Y = _dragBaseY + (w.Y - _startWorld.Y);
Canvas.SetLeft(n.Card, n.X); Canvas.SetTop(n.Card, n.Y);
UpdateNodeGeometry(n);
SetNodePos(n);
break;
case Drag.Hub:
_hubX = _dragBaseX + (w.X - _startWorld.X);
_hubY = _dragBaseY + (w.Y - _startWorld.Y);
UpdateHubGeometry();
break;
case Drag.Point:
var dxp = w.X - _startWorld.X;
var dyp = w.Y - _startWorld.Y;
var pd = _points[_dragIndex].Data;
pd.X = _dragBaseX + dxp;
pd.Y = _dragBaseY + dyp;
Canvas.SetLeft(_points[_dragIndex].Root, pd.X - 20);
Canvas.SetTop(_points[_dragIndex].Root, pd.Y - 20);
if (_points[_dragIndex].HubLine != null) { _points[_dragIndex].HubLine!.X2 = pd.X; _points[_dragIndex].HubLine!.Y2 = pd.Y; }
foreach (var (nn, bx, by) in _pointDragNodes)
{
nn.X = bx + dxp; nn.Y = by + dyp;
Canvas.SetLeft(nn.Card, nn.X); Canvas.SetTop(nn.Card, nn.Y);
UpdateNodeGeometry(nn);
SetNodePos(nn);
}
break;
case Drag.Zone:
var dx = w.X - _startWorld.X;
var dy = w.Y - _startWorld.Y;
var zz = _zones[_dragIndex].Data;
zz.X = _dragBaseX + dx;
zz.Y = _dragBaseY + dy;
Canvas.SetLeft(_zones[_dragIndex].Root, zz.X);
Canvas.SetTop(_zones[_dragIndex].Root, zz.Y);
foreach (var (nn, bx, by) in _zoneDragNodes)
{
nn.X = bx + dx; nn.Y = by + dy;
Canvas.SetLeft(nn.Card, nn.X); Canvas.SetTop(nn.Card, nn.Y);
UpdateNodeGeometry(nn);
SetNodePos(nn);
}
foreach (var (pu, bx2, by2) in _zoneDragPoints)
{
pu.Data.X = bx2 + dx; pu.Data.Y = by2 + dy;
Canvas.SetLeft(pu.Root, pu.Data.X - 20);
Canvas.SetTop(pu.Root, pu.Data.Y - 20);
if (pu.HubLine != null) { pu.HubLine.X2 = pu.Data.X; pu.HubLine.Y2 = pu.Data.Y; }
}
break;
case Drag.ZoneResize:
var zr = _zones[_dragIndex].Data;
zr.W = Math.Max(40, _startW + (w.X - _startWorld.X));
zr.H = Math.Max(40, _startH + (w.Y - _startWorld.Y));
_zones[_dragIndex].Root.Width = zr.W;
_zones[_dragIndex].Root.Height = zr.H;
break;
case Drag.Marquee:
case Drag.MarqueeZone:
UpdateMarquee(s);
break;
}
_lastScreen = s;
}
private void OnCanvasUp(object sender, MouseButtonEventArgs e)
{
switch (_drag)
{
case Drag.Pan:
if (!_moved) { _selected = -1; _multi.Clear(); _linkSel = -1; _selectedPointId = null; Render(); }
else MarkDirty();
break;
case Drag.Node:
if (!_moved) Select(_dragIndex);
else
{
var nn = _nodes[_dragIndex];
var cx = nn.X + 120; var cy = nn.Y + 46;
var near = _points.FirstOrDefault(pu => (pu.Data.X - cx) * (pu.Data.X - cx) + (pu.Data.Y - cy) * (pu.Data.Y - cy) <= 80 * 80);
if (near != null && near.Data.Id != nn.Pt?.Id) Attach(_dragIndex, near.Data.Id);
else MarkDirty();
}
break;
case Drag.Hub:
if (_moved) MarkDirty();
break;
case Drag.Point:
if (!_moved) Render(); else MarkDirty();
_pointDragNodes.Clear();
break;
case Drag.Zone:
if (!_moved) { _selectedZone = _dragIndex; ApplyZoneStyles(); }
else MarkDirty();
_zoneDragNodes.Clear();
_zoneDragPoints.Clear();
break;
case Drag.ZoneResize:
MarkDirty();
break;
case Drag.Marquee:
EndMarquee();
ApplyMarqueeSelection();
break;
case Drag.MarqueeZone:
EndMarquee();
CreateZoneFromRect();
ToggleZoneMode(false);
break;
}
_drag = Drag.None;
WorldCanvas.ReleaseMouseCapture();
}
private void BeginMarquee()
{
_marqueeRect = new Rectangle { Stroke = HubArt.B("#00d9ff"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(20, 0, 217, 255)) };
OverlayCanvas.Children.Add(_marqueeRect);
}
private void UpdateMarquee(Point s)
{
if (_marqueeRect == null) return;
var r = NormRect(_marqueeStart, s);
Canvas.SetLeft(_marqueeRect, r.X); Canvas.SetTop(_marqueeRect, r.Y);
_marqueeRect.Width = r.Width; _marqueeRect.Height = r.Height;
}
private void EndMarquee()
{
if (_marqueeRect != null) OverlayCanvas.Children.Remove(_marqueeRect);
_marqueeRect = null;
}
private static Rect NormRect(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
private void ApplyMarqueeSelection()
{
_multi.Clear();
for (int i = 0; i < _nodes.Count; i++)
{
var n = _nodes[i];
if (n.X < double.MaxValue && _multi.Contains(i)) continue;
}
_multi.Clear();
Render();
}
private void CreateZoneFromRect()
{
Render();
MarkDirty();
}
private void OnZoneHotkey()
{
var idx = _multi.Count > 0 ? _multi.ToList() : (_selected >= 0 ? new List<int> { _selected } : new List<int>());
if (idx.Count == 0) { ToggleZoneMode(!_zoneMode); return; }
double x0 = idx.Min(i => _nodes[i].X) - 24;
double y0 = idx.Min(i => _nodes[i].Y) - 40;
double x1 = idx.Max(i => _nodes[i].X + 240) + 24;
double y1 = idx.Max(i => _nodes[i].Y + 92) + 24;
_zoneDatas.Add(new ZoneData { Id = Guid.NewGuid().ToString(), Name = "ZONE " + (_zoneDatas.Count + 1), X = x0, Y = y0, W = x1 - x0, H = y1 - y0 });
RebuildAll();
MarkDirty();
}
private void OnZoneButtonClick(object sender, RoutedEventArgs e) => OnZoneHotkey();
private void ToggleZoneMode(bool on)
{
_zoneMode = on;
WorldCanvas.Cursor = on ? Cursors.Cross : Cursors.Arrow;
ZoneBtn.BorderBrush = HubArt.B(on ? "#00d9ff" : "#12404f");
}
private void ApplyZoneStyles()
{
for (int i = 0; i < _zones.Count; i++)
{
bool sel = i == _selectedZone;
var body = (Border)_zones[i].Root.Children[0];
var header = _zones[i].Header;
body.BorderBrush = HubArt.B(sel ? "#00d9ff" : "#1f6f86");
header.Background = HubArt.B(sel ? "#12404f" : "#0d2433");
header.BorderBrush = HubArt.B(sel ? "#00d9ff" : "#1f6f86");
}
}
private void DeleteZone(int i)
{
if (i < 0 || i >= _zoneDatas.Count) return;
_zoneDatas.RemoveAt(i);
_selectedZone = -1;
RebuildAll();
MarkDirty();
}
private void AddZoneUi(ZoneData z)
{
var root = new Grid { Width = z.W, Height = z.H };
Canvas.SetLeft(root, z.X); Canvas.SetTop(root, z.Y);
Panel.SetZIndex(root, 0);
var body = new Border { CornerRadius = new CornerRadius(8), BorderBrush = HubArt.B("#1f6f86"), BorderThickness = new Thickness(1), Background = HubArt.B("#141f6f86"), IsHitTestVisible = false };
var header = new Border
{
Height = 24, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
CornerRadius = new CornerRadius(6), Background = HubArt.B("#0d2433"),
BorderBrush = HubArt.B("#1f6f86"), BorderThickness = new Thickness(1),
Padding = new Thickness(8, 0, 4, 0), Cursor = Cursors.Hand, Margin = new Thickness(0, -30, 0, 0)
};
var title = new TextBlock { Text = z.Name, Foreground = HubArt.B("#8fe6ff"), FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
var close = new Button { Content = "✕", FontSize = 11, Width = 20, Height = 18, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
close.Click += (_, _) => DeleteZone(_zoneDatas.IndexOf(z));
var hs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
hs.Children.Add(title); hs.Children.Add(close);
header.Child = hs;
var handle = new Rectangle { Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 3), Fill = HubArt.B("#07141d"), Stroke = HubArt.B("#1f6f86"), StrokeThickness = 1, Cursor = Cursors.SizeNWSE };
root.Children.Add(body); root.Children.Add(header); root.Children.Add(handle);
WorldCanvas.Children.Add(root);
var zu = new ZoneUi { Data = z, Root = root, Header = header, Title = title };
_zones.Add(zu);
header.MouseLeftButtonDown += (_, e) => OnZoneDown(_zones.IndexOf(zu), e);
header.MouseLeftButtonUp += (_, e) => { if (e.ClickCount == 2) StartRename(zu); };
handle.MouseLeftButtonDown += (_, e) => OnZoneResizeDown(_zones.IndexOf(zu), e);
}
private void StartRename(ZoneUi zu)
{
var hs = (StackPanel)zu.Header.Child;
var box = new TextBox { Text = zu.Data.Name, Width = 120, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
hs.Children.Insert(0, box);
zu.Title.Visibility = Visibility.Collapsed;
box.Focus(); box.SelectAll();
bool done = false;
void Commit()
{
if (done) return;
done = true;
var t = box.Text.Trim();
if (t.Length > 0) { zu.Data.Name = t; zu.Title.Text = t; }
zu.Title.Visibility = Visibility.Visible;
hs.Children.Remove(box);
MarkDirty();
}
box.LostKeyboardFocus += (_, _) => Commit();
box.KeyDown += (_, e) =>
{
e.Handled = true;
if (e.Key == Key.Enter) Keyboard.ClearFocus();
if (e.Key == Key.Escape) { box.Text = zu.Data.Name; Keyboard.ClearFocus(); }
};
}
private void StartPointRename(PointUi pu)
{
var s = _cam.ToScreen(new Point(pu.Data.X, pu.Data.Y));
var box = new TextBox { Text = pu.Data.Name, Width = 120, FontSize = 12 };
MenuCanvas.Children.Add(box);
Canvas.SetLeft(box, s.X - 60);
Canvas.SetTop(box, s.Y - 44);
box.Focus(); box.SelectAll();
bool done = false;
void Commit()
{
if (done) return;
done = true;
var t = box.Text.Trim();
if (t.Length > 0) pu.Data.Name = t;
MenuCanvas.Children.Remove(box);
Render();
MarkDirty();
}
box.LostKeyboardFocus += (_, _) => Commit();
box.KeyDown += (_, e) =>
{
e.Handled = true;
if (e.Key == Key.Enter) Keyboard.ClearFocus();
if (e.Key == Key.Escape) { box.Text = pu.Data.Name; Keyboard.ClearFocus(); }
};
}
// Раскладка v3: веера вокруг точек; точки и свободные карточки — сеткой в зонах / кольцом у хаба.
private void AlignAll()
{
foreach (var pu in _points)
{
var cards = _nodes.Where(n => n.Pt == pu.Data).ToList();
if (cards.Count == 0) continue;
double baseA = Math.Atan2(pu.Data.Y - (_hubY + 48), pu.Data.X - (_hubX + 48));
double r = 170 + 10 * cards.Count;
for (int k = 0; k < cards.Count; k++)
{
double a = baseA + k * 2 * Math.PI / cards.Count;
var n = cards[k];
n.X = pu.Data.X + r * Math.Cos(a) - 120;
n.Y = pu.Data.Y + r * Math.Sin(a) - 46;
Canvas.SetLeft(n.Card, n.X); Canvas.SetTop(n.Card, n.Y);
UpdateNodeGeometry(n);
SetNodePos(n);
}
}
var units = new List<(bool IsPt, PointUi? Pu, NodeUi? Nu, double Cx, double Cy)>();
foreach (var pu in _points) units.Add((true, pu, null, pu.Data.X, pu.Data.Y));
foreach (var n in _nodes) if (n.Pt == null) units.Add((false, null, n, n.X + 120, n.Y + 46));
var used = new bool[units.Count];
foreach (var zu in _zones)
{
var z = zu.Data;
var idxs = Enumerable.Range(0, units.Count)
.Where(i => !used[i] && units[i].Cx > z.X && units[i].Cx < z.X + z.W && units[i].Cy > z.Y && units[i].Cy < z.Y + z.H)
.OrderBy(i => units[i].Cx).ThenBy(i => units[i].Cy).ToList();
if (idxs.Count == 0) continue;
int cols = (int)Math.Ceiling(Math.Sqrt(idxs.Count));
int rows = (int)Math.Ceiling(idxs.Count / (double)cols);
for (int k = 0; k < idxs.Count; k++)
{
double cx = z.X + 60 + (k % cols) * 520 + 260;
double cy = z.Y + 80 + (k / cols) * 420 + 210;
MoveUnit(units[idxs[k]], cx, cy);
used[idxs[k]] = true;
}
z.W = Math.Max(z.W, 120 + cols * 520);
z.H = Math.Max(z.H, 160 + rows * 420);
zu.Root.Width = z.W; zu.Root.Height = z.H;
}
var rest = Enumerable.Range(0, units.Count).Where(i => !used[i])
.OrderBy(i => Math.Atan2(units[i].Cy - (_hubY + 48), units[i].Cx - (_hubX + 48))).ToList();
for (int k = 0; k < rest.Count; k++)
{
double a = (-135 + k * 360.0 / rest.Count) * Math.PI / 180;
MoveUnit(units[rest[k]], _hubX + 48 + 340 * Math.Cos(a), _hubY + 48 + 260 * Math.Sin(a));
}
MarkDirty();
}
private void MoveUnit((bool IsPt, PointUi? Pu, NodeUi? Nu, double Cx, double Cy) u, double cx, double cy)
{
double dx = cx - u.Cx, dy = cy - u.Cy;
if (u.IsPt)
{
var pu = u.Pu!;
pu.Data.X += dx; pu.Data.Y += dy;
Canvas.SetLeft(pu.Root, pu.Data.X - 20);
Canvas.SetTop(pu.Root, pu.Data.Y - 20);
if (pu.HubLine != null) { pu.HubLine.X2 = pu.Data.X; pu.HubLine.Y2 = pu.Data.Y; }
foreach (var n in _nodes.Where(n => n.Pt == pu.Data))
{
n.X += dx; n.Y += dy;
Canvas.SetLeft(n.Card, n.X); Canvas.SetTop(n.Card, n.Y);
UpdateNodeGeometry(n);
SetNodePos(n);
}
}
else
{
var n = u.Nu!;
n.X += dx; n.Y += dy;
Canvas.SetLeft(n.Card, n.X); Canvas.SetTop(n.Card, n.Y);
UpdateNodeGeometry(n);
SetNodePos(n);
}
}
private void OnWheel(object sender, MouseWheelEventArgs e)
{
_cam.SetZoom(_cam.Zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), e.GetPosition(OverlayCanvas));
ApplyCamera();
MarkDirty();
e.Handled = true;
}
private void OnZoomInClick(object sender, RoutedEventArgs e) { _cam.SetZoom(_cam.Zoom * 1.15, ViewCenter); ApplyCamera(); MarkDirty(); }
private void OnZoomOutClick(object sender, RoutedEventArgs e) { _cam.SetZoom(_cam.Zoom / 1.15, ViewCenter); ApplyCamera(); MarkDirty(); }
private void OnZoomTextClick(object sender, MouseButtonEventArgs e)
{
ZoomInput.Text = ((int)Math.Round(_cam.Zoom * 100)).ToString();
ZoomText.Visibility = Visibility.Collapsed;
ZoomInput.Visibility = Visibility.Visible;
ZoomInput.Focus(); ZoomInput.SelectAll();
}
private void HideZoomInput()
{
ZoomInput.Visibility = Visibility.Collapsed;
ZoomText.Visibility = Visibility.Visible;
}
private void OnZoomInputKeyDown(object sender, KeyEventArgs e)
{
e.Handled = true;
if (e.Key == Key.Enter && int.TryParse(ZoomInput.Text, out var v))
{
_cam.SetZoom(Math.Clamp(v, 25, 400) / 100.0, ViewCenter);
ApplyCamera();
MarkDirty();
}
if (e.Key == Key.Enter || e.Key == Key.Escape) HideZoomInput();
}
private void OnZoomInputLostFocus(object sender, RoutedEventArgs e) => HideZoomInput();
private void OnFitAllClick(object sender, RoutedEventArgs e) => FitAll();
private void FitAll()
{
double x0 = _hubX, y0 = _hubY, x1 = _hubX + 96, y1 = _hubY + 96;
foreach (var n in _nodes) { x0 = Math.Min(x0, n.X); y0 = Math.Min(y0, n.Y); x1 = Math.Max(x1, n.X + 240); y1 = Math.Max(y1, n.Y + 92); }
foreach (var pu in _points) { x0 = Math.Min(x0, pu.Data.X - 60); y0 = Math.Min(y0, pu.Data.Y - 60); x1 = Math.Max(x1, pu.Data.X + 60); y1 = Math.Max(y1, pu.Data.Y + 80); }
foreach (var z in _zones) { x0 = Math.Min(x0, z.Data.X); y0 = Math.Min(y0, z.Data.Y); x1 = Math.Max(x1, z.Data.X + z.Data.W); y1 = Math.Max(y1, z.Data.Y + z.Data.H); }
double w = OverlayCanvas.ActualWidth, h = OverlayCanvas.ActualHeight;
if (w < 10 || h < 10) return;
double bw = x1 - x0 + 160, bh = y1 - y0 + 160;
_cam.Zoom = Math.Clamp(Math.Min(w / bw, h / bh), HubCamera.MinZoom, HubCamera.MaxZoom);
_cam.PanX = w / 2 - (x0 + x1) / 2 * _cam.Zoom;
_cam.PanY = h / 2 - (y0 + y1) / 2 * _cam.Zoom;
ApplyCamera();
MarkDirty();
}
}