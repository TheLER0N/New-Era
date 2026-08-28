using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
namespace MainApp;
public partial class ProjectHubWindow : ChromeWindow
{
public static ProjectHubWindow? Current { get; private set; }
private List<ProjectEntry> _projects = new();
private readonly List<string?> _pointOf = new();
private int _selected = -1;
private readonly HashSet<int> _multi = new();
private int _selectedZone = -1;
private string? _selectedPointId;
private int _linkSel = -1;
private bool _moveMode;
private int _moveCard = -1;
private List<ZoneData> _zoneDatas = new();
private List<PointData> _pointDatas = new();
private readonly Dictionary<string, (double X, double Y)> _pos = new();
private readonly HubCamera _cam = new();
private readonly List<NodeUi> _nodes = new();
private readonly List<ZoneUi> _zones = new();
private readonly List<PointUi> _points = new();
private FrameworkElement? _hubUi;
private double _hubX, _hubY;
private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
private bool _ready;
private Border? _pointMenu;
public class NodeUi
{
public ProjectEntry P = null!;
public Border Card = null!;
public Line? Link;
public Line? Hit;
public PointData? Pt;
public double X, Y;
}
public class ZoneUi { public ZoneData Data = null!; public Grid Root = null!; public Border Header = null!; public TextBlock Title = null!; }
public class PointUi { public PointData Data = null!; public Grid Root = null!; public Line? HubLine; }
public ProjectHubWindow()
{
InitializeComponent();
_saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveLayout(); };
Loaded += (_, _) =>
{
if (Current != null && Current != this) Current.Close();
Current = this;
_ = GatewayLauncher.EnsureRunningAsync();
InitCanvas();
LoadLayout();
_ready = true;
Render();
ApplyCamera();
};
Closed += (_, _) =>
{
_saveTimer.Stop();
SaveLayout();
if (Current == this) Current = null;
if (Application.Current?.Windows.Count == 1) Application.Current.Shutdown();
};
_projects = ProjectStore.Load();
if (_projects.Count > 0)
{
var last = _projects.OrderByDescending(p => p.LastOpened ?? DateTime.MinValue).First();
_selected = _projects.IndexOf(last);
}
}
private void Render()
{HideEmptyHint();
ClosePointMenu();
CountText.Text = _projects.Count.ToString();
EmptyState.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
if (_selected >= _projects.Count) _selected = -1;
FooterLeft.Text = _selected >= 0
? $"Выбран: {_projects[_selected].Name} · Enter — открыть"
: _projects.Count == 0 ? "Нет проектов — нажми N" : "Выбери проект";
RebuildAll();
}
private (double X, double Y) GetPos(ProjectEntry p)
{
var key = p.Path.ToLowerInvariant();
if (!_pos.ContainsKey(key))
{
double a = (-135 + _pos.Count * 90.0) * Math.PI / 180;
_pos[key] = (_hubX + 48 + 260 * Math.Cos(a) - 120, _hubY + 48 + 260 * Math.Sin(a) - 46);
}
return _pos[key];
}
private void RebuildAll()
{
WorldCanvas.Children.Clear();
_nodes.Clear();
_zones.Clear();
_points.Clear();
_selectedZone = -1;
foreach (var z in _zoneDatas) AddZoneUi(z);
_hubUi = HubArt.HubCore();
Canvas.SetLeft(_hubUi, _hubX);
Canvas.SetTop(_hubUi, _hubY);
Panel.SetZIndex(_hubUi, 2);
_hubUi.MouseLeftButtonDown += OnHubDown;
WorldCanvas.Children.Add(_hubUi);
foreach (var pd in _pointDatas) AddPointUi(pd);
for (int i = 0; i < _projects.Count; i++) AddNodeUi(i);
}
private int PointCount(string id)
{
int c = 0;
for (int i = 0; i < _projects.Count; i++) if (_pointOf[i] == id) c++;
return c;
}
private void AddPointUi(PointData pd)
{
int count = PointCount(pd.Id);
bool sel = pd.Id == _selectedPointId || _multiPts.Contains(pd.Id);
var dot = HubArt.PointDot(pd.Name, count, count == 0, sel);
var pu = new PointUi { Data = pd, Root = dot };
Canvas.SetLeft(dot, pd.X - 20);
Canvas.SetTop(dot, pd.Y - 20);
Panel.SetZIndex(dot, 2);
if (count > 0)
{
var hl = new Line { X1 = _hubX + 48, Y1 = _hubY + 48, X2 = pd.X, Y2 = pd.Y, Stroke = HubArt.B("#12404f"), StrokeThickness = 1, Opacity = 0.6 };
Panel.SetZIndex(hl, 1);
WorldCanvas.Children.Add(hl);
pu.HubLine = hl;
}
WorldCanvas.Children.Add(dot);
_points.Add(pu);
pu.Root.ContextMenu = PointMenu(pu.Data);
int pi = _points.Count - 1;
dot.MouseLeftButtonDown += (_, e) => OnPointDown(pi, e);
dot.MouseLeftButtonUp += (_, e) => { if (e.ClickCount == 2) StartPointRename(pu); };
}
private void AddNodeUi(int i)
{
var p = _projects[i];
bool sel = i == _selected, multi = _multi.Contains(i);
var card = HubArt.FolderCard(BuildView(p), sel, multi);
var (x, y) = GetPos(p);
Panel.SetZIndex(card, sel ? 4 : 3);
Canvas.SetLeft(card, x);
Canvas.SetTop(card, y);
int idx = i;
card.MouseLeftButtonDown += (_, e) => OnNodeDown(idx, e);
card.ContextMenu = NodeMenu(idx);
var n = new NodeUi { P = p, Card = card, X = x, Y = y };
string? pid = i < _pointOf.Count ? _pointOf[i] : null;
if (pid != null)
{
var pt = _pointDatas.FirstOrDefault(d => d.Id == pid);
if (pt != null)
{
n.Pt = pt;
n.Link = new Line { X1 = pt.X, Y1 = pt.Y, X2 = x + 120, Y2 = y + 46, Stroke = HubArt.B(idx == _linkSel ? "#e94560" : "#1f6f86"), StrokeThickness = idx == _linkSel ? 2 : 1, Opacity = 0.8 };
Panel.SetZIndex(n.Link, 1);
WorldCanvas.Children.Add(n.Link);
n.Hit = new Line { X1 = pt.X, Y1 = pt.Y, X2 = x + 120, Y2 = y + 46, Stroke = new SolidColorBrush(Colors.Transparent), StrokeThickness = 10, Cursor = Cursors.Hand };
Panel.SetZIndex(n.Hit, 4);
n.Hit.MouseLeftButtonDown += (_, _) => OnLinkDown(idx);
WorldCanvas.Children.Add(n.Hit);
}
}
WorldCanvas.Children.Add(card);
_nodes.Add(n);
UpdateNodeGeometry(n);
}
private void UpdateNodeGeometry(NodeUi n)
{
if (n.Link != null && n.Pt != null)
{
n.Link.X1 = n.Pt.X; n.Link.Y1 = n.Pt.Y;
n.Link.X2 = n.X + 120; n.Link.Y2 = n.Y + 46;
}
if (n.Hit != null && n.Pt != null)
{
n.Hit.X1 = n.Pt.X; n.Hit.Y1 = n.Pt.Y;
n.Hit.X2 = n.X + 120; n.Hit.Y2 = n.Y + 46;
}
}
private void UpdateHubGeometry()
{
if (_hubUi != null) { Canvas.SetLeft(_hubUi, _hubX); Canvas.SetTop(_hubUi, _hubY); }
foreach (var pu in _points) if (pu.HubLine != null) { pu.HubLine.X1 = _hubX + 48; pu.HubLine.Y1 = _hubY + 48; }
}
private static HubArt.ProjectView BuildView(ProjectEntry p)
{
var v = new HubArt.ProjectView { Name = p.Name, PathLine = p.Path };
try
{
if (Directory.Exists(p.Path))
{
var dirs = Directory.GetDirectories(p.Path).Select(d => System.IO.Path.GetFileName(d)).Where(n => !string.IsNullOrEmpty(n) && !n.StartsWith(".")).ToList();
var files = Directory.GetFiles(p.Path).Select(f => System.IO.Path.GetFileName(f)).Where(n => !string.IsNullOrEmpty(n)).ToList();
v.StatsLine = $"{files.Count} файлов · {dirs.Count} папок";
v.ToolTip = files.Count > 0 ? string.Join("\n", files.Take(15)) : "Файлов нет";
}
else v.StatsLine = "папка не найдена";
}
catch { v.StatsLine = "—"; }
v.TimeLine = FormatRelative(p.LastOpened);
return v;
}
private void Select(int i) { _selected = i; _multi.Clear(); _multiPts.Clear(); _multiZns.Clear(); _linkSel = -1; Render(); }
private void MarkDirty() { _saveTimer.Stop(); _saveTimer.Start(); }
private void LoadLayout()
{
_pointOf.Clear();
var lay = HubLayoutStore.Load();
_cam.PanX = lay.PanX; _cam.PanY = lay.PanY;
_cam.Zoom = lay.Zoom < HubCamera.MinZoom || lay.Zoom > HubCamera.MaxZoom ? 1 : lay.Zoom;
double w = WorldCanvas.ActualWidth > 10 ? WorldCanvas.ActualWidth : 900;
double h = WorldCanvas.ActualHeight > 10 ? WorldCanvas.ActualHeight : 560;
bool trusted = lay.Ver >= 2;
bool hasHub = trusted && (lay.HubX != 0 || lay.HubY != 0);
_hubX = hasHub ? lay.HubX : w / 2 - 48;
_hubY = hasHub ? lay.HubY : h / 2 - 48;
_zoneDatas = trusted ? lay.Zones ?? new List<ZoneData>() : new List<ZoneData>();
_pointDatas = lay.Points ?? new List<PointData>();
if (trusted && lay.Ver < 3 && _pointDatas.Count == 0)
{
int nn = 1;
foreach (var np in lay.Nodes)
{
var pid = "pt-" + nn + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
_pointDatas.Add(new PointData { Id = pid, Name = "POINT " + nn, X = np.X, Y = np.Y });
np.PointId = pid;
double ddx = np.X - (_hubX + 48), ddy = np.Y - (_hubY + 48);
double len = Math.Sqrt(ddx * ddx + ddy * ddy);
if (len < 1) { ddx = 1; ddy = 0; len = 1; }
np.X += ddx / len * 190; np.Y += ddy / len + 190;
nn++;
}
}
if (trusted)
{
var byPath = new Dictionary<string, NodePos>();
foreach (var np in lay.Nodes) byPath[np.Path.ToLowerInvariant()] = np;
foreach (var p in _projects)
{
if (byPath.TryGetValue(p.Path.ToLowerInvariant(), out var np))
{
_pos[p.Path.ToLowerInvariant()] = (np.X, np.Y);
_pointOf.Add(np.PointId);
}
else _pointOf.Add(null);
}
}
else foreach (var p in _projects) _pointOf.Add(null);
}
private void SaveLayout()
{
if (!_ready) return;
HubLayoutStore.Save(new HubLayout
{
PanX = _cam.PanX, PanY = _cam.PanY, Zoom = _cam.Zoom, Ver = 3,
HubX = _hubX, HubY = _hubY,
Nodes = _nodes.Select((n, i) => new NodePos { Path = n.P.Path, X = n.X, Y = n.Y, PointId = i < _pointOf.Count ? _pointOf[i] : null }).ToList(),
Zones = _zoneDatas,
Points = _pointDatas
});
}
private void CreatePoint()
{
var c = _cam.ToWorld(new Point(OverlayCanvas.ActualWidth / 2, OverlayCanvas.ActualHeight / 2));
_pointDatas.Add(new PointData { Id = "pt-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = "POINT " + (_pointDatas.Count + 1), X = c.X, Y = c.Y });
Render();
MarkDirty();
}
private void DeletePoint(string id)
{
for (int i = 0; i < _pointOf.Count; i++) if (_pointOf[i] == id) _pointOf[i] = null;
_pointDatas.RemoveAll(d => d.Id == id);
_selectedPointId = null;
Render();
MarkDirty();
}
private void Attach(int cardIdx, string pointId)
{
if (cardIdx < 0 || cardIdx >= _pointOf.Count) return;
_pointOf[cardIdx] = pointId;
_linkSel = -1;
Render();
MarkDirty();
}
private void AttachNewProject(string pointId)
{
var dlg = new OpenFolderDialog { Title = "Выбери папку проекта" };
if (dlg.ShowDialog() != true) return;
var path = dlg.FolderName;
var existing = _projects.FirstOrDefault(p =>
string.Equals(Norm(p.Path), Norm(path), StringComparison.OrdinalIgnoreCase));
int index;
if (existing != null) index = _projects.IndexOf(existing);
else
{
_projects.Add(new ProjectEntry { Name = new DirectoryInfo(path).Name, Path = path, Role = "team" });
_pointOf.Add(null);
index = _projects.Count - 1;
ProjectStore.Save(_projects);
}
_pointOf[index] = pointId;
var pt = _pointDatas.FirstOrDefault(d => d.Id == pointId);
if (pt != null) _pos[path.ToLowerInvariant()] = (pt.X + 190, pt.Y);
Render();
MarkDirty();
}
private void ShowPointMenu(PointData pd)
{
ClosePointMenu();
var s = _cam.ToScreen(new Point(pd.X, pd.Y));
var menu = new Border { Background = HubArt.B("#07141d"), BorderBrush = HubArt.B("#12404f"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6) };
var sp = new StackPanel();
var b1 = new Button { Content = "прикрепить проект", FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 4) };
var b2 = new Button { Content = "удалить точку", FontSize = 12, Padding = new Thickness(8, 4, 8, 4) };
b1.Click += (_, _) => AttachNewProject(pd.Id);
b2.Click += (_, _) => DeletePoint(pd.Id);
sp.Children.Add(b1);
sp.Children.Add(b2);
menu.Child = sp;
MenuCanvas.Children.Add(menu);
Canvas.SetLeft(menu, s.X + 24);
Canvas.SetTop(menu, s.Y - 20);
_pointMenu = menu;
}
private void ClosePointMenu()
{
if (_pointMenu != null) MenuCanvas.Children.Remove(_pointMenu);
_pointMenu = null;
}
private void ExitMoveMode()
{
_moveMode = false;
_moveCard = -1;
WorldCanvas.Cursor = Cursors.Arrow;
}
private void OpenProject(ProjectEntry p)
{
p.LastOpened = DateTime.Now;
ProjectStore.Save(_projects);
SaveLayout();
var main = new MainWindow(p.Path, p.Name);
main.StartFullscreen = false;
main.UseFadeIn = false;
CopyGeometry(main);
SwapTo(main);
}
private void CopyGeometry(Window w)
{
w.WindowStartupLocation = WindowStartupLocation.Manual;
w.Left = Left; w.Top = Top; w.Width = Width; w.Height = Height;
}
private void DeleteProject(int i)
{
if (i < 0 || i >= _projects.Count) return;
var p = _projects[i];
var res = MessageBox.Show(
$"Убрать \"{p.Name}\" из списка проектов?\nПапка и файлы останутся на диске.\nИстория чата этого проекта будет стёрта.",
"LERON GUI", MessageBoxButton.YesNo, MessageBoxImage.Question);
if (res != MessageBoxResult.Yes) return;
_pos.Remove(p.Path.ToLowerInvariant());
_projects.RemoveAt(i);
_pointOf.RemoveAt(i);
ProjectStore.Save(_projects);
HistoryStore.DeleteProjectHistory(p.Path);
_selected = -1;
_linkSel = -1;
Render();
MarkDirty();
}
private void CreateNewProject()
{
var dlg = new OpenFolderDialog { Title = "Выбери папку проекта" };
if (dlg.ShowDialog() != true) return;
var path = dlg.FolderName;
var existing = _projects.FirstOrDefault(p =>
string.Equals(Norm(p.Path), Norm(path), StringComparison.OrdinalIgnoreCase));
int index;
if (existing != null) index = _projects.IndexOf(existing);
else
{
_projects.Add(new ProjectEntry { Name = new DirectoryInfo(path).Name, Path = path, Role = "team" });
_pointOf.Add(null);
index = _projects.Count - 1;
var c = _cam.ToWorld(new Point(OverlayCanvas.ActualWidth / 2, OverlayCanvas.ActualHeight / 2));
_pos[path.ToLowerInvariant()] = (c.X - 120 + 24, c.Y - 46 + 24);
}
ProjectStore.Save(_projects);
Render();
Select(index);
}
private static string Norm(string p) => p.TrimEnd('\\', '/');
private static string FormatRelative(DateTime? dt)
{
if (dt == null) return "ещё не открыт";
var span = DateTime.Now - dt.Value;
if (span.TotalMinutes < 1) return "только что";
if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} мин назад";
if (dt.Value.Date == DateTime.Today) return $"{(int)span.TotalHours} ч назад";
if (dt.Value.Date == DateTime.Today.AddDays(-1)) return $"вчера, {dt:HH:mm}";
if (span.TotalDays < 7) return $"{(int)span.TotalDays} дн назад";
return dt.Value.ToString("dd.MM.yyyy");
}
private void OnKeyDown(object sender, KeyEventArgs e)
{
if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;
switch (e.Key)
{ case Key.Up: Select(Math.Max(0, _selected - 1)); e.Handled = true; break;
case Key.Down: Select(Math.Min(_projects.Count - 1, _selected + 1)); e.Handled = true; break;
case Key.Left: Select(Math.Max(0, _selected - 1)); e.Handled = true; break;
case Key.Right: Select(Math.Min(_projects.Count - 1, _selected + 1)); e.Handled = true; break;
case Key.Enter: if (_selected >= 0) OpenProject(_projects[_selected]); else CreateNewProject(); e.Handled = true; break;
case Key.N: CreateNewProject(); e.Handled = true; break;
case Key.T: CreatePoint(); e.Handled = true; break;
case Key.M:
if (_selected >= 0)
{
_moveMode = true;
_moveCard = _selected;
WorldCanvas.Cursor = Cursors.Cross;
FooterLeft.Text = "M: кликни точку — карточка прикрепится · Escape — отмена";
}
e.Handled = true; break;
case Key.S: OnConfigureClick(this, new RoutedEventArgs()); e.Handled = true; break;
case Key.Z: ToggleZoneMode(!_zoneMode); e.Handled = true; break;
case Key.F: FitAll(); e.Handled = true; break;
case Key.A:
if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)) AlignAll();
else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) { _multi.Clear(); for (int i = 0; i < _projects.Count; i++) _multi.Add(i); if (_projects.Count > 0) _selected = 0; Render(); }
e.Handled = true; break;
case Key.Delete:
if (_multi.Count > 0 || _multiPts.Count > 0 || _multiZns.Count > 0) { DeleteMulti(); e.Handled = true; break; }
if (_linkSel >= 0) { _pointOf[_linkSel] = null; _linkSel = -1; Render(); MarkDirty(); }
else if (_selectedPointId != null) DeletePoint(_selectedPointId);
else if (_selectedZone >= 0) DeleteZone(_selectedZone);
else DeleteProject(_selected);
e.Handled = true; break;
case Key.Q: Application.Current.Shutdown(); break;
case Key.Escape:
if (_moveMode) { ExitMoveMode(); Render(); }
else if (_pointMenu != null) ClosePointMenu();
else if (_zoneMode) ToggleZoneMode(false);
else { _selected = -1; _multi.Clear(); _multiPts.Clear(); _multiZns.Clear(); _linkSel = -1; _selectedPointId = null; Render(); }
e.Handled = true; break;
}
}
private void OnPointButtonClick(object sender, RoutedEventArgs e) => CreatePoint();
private void OnNewProjectButtonClick(object sender, RoutedEventArgs e) => CreateNewProject();
private void OnConfigureClick(object sender, RoutedEventArgs e)
{
if (_selected >= 0 && _selected < _projects.Count) OpenProject(_projects[_selected]);
else FooterLeft.Text = "Сначала выбери проект (клик по папке) или создай новый (N).";
}
private void OnExitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
public class ProjectEntry
{
public string Name { get; set; } = "";
public string Path { get; set; } = "";
public string Role { get; set; } = "team";
public DateTime? LastOpened { get; set; }
}
public static class ProjectStore
{
public static List<ProjectEntry> Load()
{
try
{
var path = BrowserLauncher.GetConfigPath();
if (path == null) return new();
var node = JsonNode.Parse(File.ReadAllText(path));
var arr = node?["Projects"] as JsonArray;
if (arr == null) return new();
return System.Text.Json.JsonSerializer.Deserialize<List<ProjectEntry>>(
arr.ToJsonString(),
new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}
catch { return new(); }
}
public static void Save(List<ProjectEntry> projects)
{
try
{
var path = BrowserLauncher.GetConfigPath();
if (path == null) return;
var node = File.ReadAllText(path) is string s ? JsonNode.Parse(s)?.AsObject() : null;
if (node == null) return;
node["Projects"] = System.Text.Json.JsonSerializer.SerializeToNode(projects);
File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
catch { }
}
}