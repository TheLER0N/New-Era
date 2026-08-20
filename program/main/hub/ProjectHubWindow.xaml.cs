using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
namespace MainApp;
public partial class ProjectHubWindow : ChromeWindow
{
public static ProjectHubWindow? Current { get; private set; }
private List<ProjectEntry> _projects = new();
private readonly List<System.Windows.Controls.Border> _rows = new();
private int _selected = -1;
public ProjectHubWindow()
{
InitializeComponent();
Loaded += (_, _) =>
{
if (Current != null && Current != this) Current.Close();
Current = this;
_ = GatewayLauncher.EnsureRunningAsync();
};
Closed += (_, _) =>
{
if (Current == this) Current = null;
if (Application.Current?.Windows.Count == 1) Application.Current.Shutdown();
};
_projects = ProjectStore.Load();
Render();
if (_projects.Count > 0)
{
var last = _projects.OrderByDescending(p => p.LastOpened ?? DateTime.MinValue).First();
Select(_projects.IndexOf(last));
}
}
private void Render()
{
ProjectsPanel.Children.Clear();
_rows.Clear();
CountText.Text = _projects.Count.ToString();
for (int i = 0; i < _projects.Count; i++)
{
var p = _projects[i];
var row = new System.Windows.Controls.Border
{
Background = Brush("#0c1710"),
BorderBrush = Brush("#142a1e"),
BorderThickness = new Thickness(1),
CornerRadius = new CornerRadius(10),
Margin = new Thickness(0, 0, 0, 16),
Padding = new Thickness(18),
Cursor = Cursors.Hand,
Tag = i
};
row.MouseLeftButtonUp += (_, _) => OpenProject(p);
row.MouseEnter += (_, _) => { if ((int)row.Tag != _selected) row.BorderBrush = Brush("#2a5a40"); };
row.MouseLeave += (_, _) => { if ((int)row.Tag != _selected) row.BorderBrush = Brush("#142a1e"); };
var grid = new Grid();
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
var preview = GetFilesPreview(p.Path);
var iconBox = new System.Windows.Controls.Border
{
Width = 54,
Height = 54,
CornerRadius = new CornerRadius(10),
Background = Brush("#123020"),
BorderBrush = Brush("#1d5c3d"),
BorderThickness = new Thickness(1),
Margin = new Thickness(0, 0, 16, 0)
};
iconBox.Child = new TextBlock
{
Text = "📁",
FontSize = 24,
HorizontalAlignment = HorizontalAlignment.Center,
VerticalAlignment = VerticalAlignment.Center
};
Grid.SetColumn(iconBox, 0);
var texts = new StackPanel();
texts.Children.Add(new TextBlock { Text = p.Name, Foreground = Brush("#4ade80"), FontSize = 18, FontWeight = FontWeights.Bold });
texts.Children.Add(new TextBlock { Text = p.Path, Foreground = Brush("#4a7a5a"), FontSize = 13, Margin = new Thickness(0, 5, 0, 0) });
texts.Children.Add(new TextBlock
{
Text = "○ " + preview.Line,
Foreground = Brush("#3d6a4d"),
FontSize = 12,
Margin = new Thickness(0, 4, 0, 0),
TextTrimming = TextTrimming.CharacterEllipsis
});
Grid.SetColumn(texts, 1);
var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
right.Children.Add(new TextBlock { Text = FormatRelative(p.LastOpened), Foreground = Brush("#4a7a5a"), FontSize = 13, Margin = new Thickness(0, 0, 14, 0) });
var badge = new System.Windows.Controls.Border
{
Background = Brush("#123020"),
CornerRadius = new CornerRadius(8),
Padding = new Thickness(12, 7, 12, 7),
ToolTip = preview.ToolTip
};
badge.Child = new TextBlock { Text = preview.Badge, Foreground = Brush("#7dffa8"), FontSize = 13 };
right.Children.Add(badge);
var del = new Button
{
Content = "✕",
ToolTip = "Убрать проект из списка и стереть его историю чата",
Background = Brush("#1a0f14"),
Foreground = Brush("#e94560"),
BorderThickness = new Thickness(0),
Cursor = Cursors.Hand,
FontSize = 14,
Padding = new Thickness(9, 4, 9, 4),
Margin = new Thickness(12, 0, 0, 0),
VerticalAlignment = VerticalAlignment.Center
};
int idx = i;
del.Click += (_, _) => DeleteProject(idx);
right.Children.Add(del);
Grid.SetColumn(right, 2);
grid.Children.Add(iconBox);
grid.Children.Add(texts);
grid.Children.Add(right);
row.Child = grid;
_rows.Add(row);
ProjectsPanel.Children.Add(row);
}
UpdateSelection();
}
private static (string Badge, string Line, string ToolTip) GetFilesPreview(string path)
{
try
{
if (!Directory.Exists(path))
return ("📄 папка не найдена", "пусто", "");
var files = Directory.GetFiles(path)
.Select(Path.GetFileName)
.Where(n => !string.IsNullOrEmpty(n))
.Select(n => n!)
.ToList();
var dirs = Directory.GetDirectories(path)
.Select(Path.GetFileName)
.Where(n => !string.IsNullOrEmpty(n))
.ToList();
var badge = $"📄 {files.Count} файлов";
var shown = string.Join(", ", files.Take(4));
var line = dirs.Count > 0
? $"[{dirs.Count} папок] {shown}"
: shown;
if (string.IsNullOrWhiteSpace(shown))
line = dirs.Count > 0 ? $"[{dirs.Count} папок]" : "пусто";
var tooltipList = files.Take(15).ToList();
var tooltip = tooltipList.Count > 0
? string.Join("\n", tooltipList)
: "Файлов нет";
return (badge, line, tooltip);
}
catch
{
return ("📄 —", "пусто", "");
}
}
private void Select(int i) { _selected = i; UpdateSelection(); }
private void UpdateSelection()
{
for (int i = 0; i < _rows.Count; i++)
{
bool sel = i == _selected;
_rows[i].BorderBrush = Brush(sel ? "#00ff88" : "#142a1e");
_rows[i].BorderThickness = new Thickness(sel ? 1.5 : 1);
_rows[i].Background = Brush(sel ? "#10241a" : "#0c1710");
}
FooterLeft.Text = _selected >= 0 && _selected < _projects.Count
? $"Выбран: {_projects[_selected].Name} · Enter — открыть"
: _projects.Count == 0 ? "Нет проектов — нажми N" : "Выбери проект";
}
private void OpenProject(ProjectEntry p)
{
p.LastOpened = DateTime.Now;
ProjectStore.Save(_projects);
var main = new MainWindow(p.Path, p.Name);
main.StartFullscreen = false;
main.UseFadeIn = false;
CopyGeometry(main);
SwapTo(main);
}
private void CopyGeometry(Window w)
{
w.WindowStartupLocation = WindowStartupLocation.Manual;
w.Left = Left;
w.Top = Top;
w.Width = Width;
w.Height = Height;
}
private void DeleteProject(int i)
{
if (i < 0 || i >= _projects.Count) return;
var p = _projects[i];
var res = MessageBox.Show(
$"Убрать \"{p.Name}\" из списка проектов?\nПапка и файлы останутся на диске.\nИстория чата этого проекта будет стёрта.",
"LERON GUI",
MessageBoxButton.YesNo,
MessageBoxImage.Question);
if (res != MessageBoxResult.Yes) return;
_projects.RemoveAt(i);
ProjectStore.Save(_projects);
HistoryStore.DeleteProjectHistory(p.Path);
_selected = -1;
Render();
}
private void CreateNewProject()
{
var dlg = new OpenFolderDialog { Title = "Выбери папку проекта" };
if (dlg.ShowDialog() != true) return;
var path = dlg.FolderName;
var existing = _projects.FirstOrDefault(p =>
string.Equals(Norm(p.Path), Norm(path), StringComparison.OrdinalIgnoreCase));
int index;
if (existing != null)
{
index = _projects.IndexOf(existing);
}
else
{
_projects.Add(new ProjectEntry
{
Name = new DirectoryInfo(path).Name,
Path = path,
Role = "team"
});
index = _projects.Count - 1;
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
private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
private void OnKeyDown(object sender, KeyEventArgs e)
{
switch (e.Key)
{
case Key.Up: Select(Math.Max(0, _selected - 1)); e.Handled = true; break;
case Key.Down: Select(Math.Min(_projects.Count - 1, _selected + 1)); e.Handled = true; break;
case Key.Enter: if (_selected >= 0) OpenProject(_projects[_selected]); else CreateNewProject(); e.Handled = true; break;
case Key.N: CreateNewProject(); e.Handled = true; break;
case Key.S: OnConfigureClick(this, new RoutedEventArgs()); e.Handled = true; break;
case Key.Delete: DeleteProject(_selected); e.Handled = true; break;
case Key.Q: Application.Current.Shutdown(); break;
case Key.Escape: Close(); break;
}
}
private void OnNewProjectClick(object sender, MouseButtonEventArgs e) => CreateNewProject();
private void OnNewProjectButtonClick(object sender, RoutedEventArgs e) => CreateNewProject();
private void OnConfigureClick(object sender, RoutedEventArgs e)
{
if (_selected >= 0 && _selected < _projects.Count)
OpenProject(_projects[_selected]);
else
FooterLeft.Text = "Сначала выбери проект (клик по строке) или создай новый (N).";
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
var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
if (node == null) return;
node["Projects"] = System.Text.Json.JsonSerializer.SerializeToNode(projects);
File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
catch { }
}
}