using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Gateway;
using MainApp;

namespace Hub
{
public partial class HubSettingsPage : UserControl
{
public event Action? BackRequested;

public HubSettingsPage()
{
InitializeComponent();
Loaded += (_, _) => Focus();
}

public void Reload()
{
UserProfile.Exists();
TxtUsername.Text = UserProfile.Nick;
TxtDescription.Text = UserProfile.Description;
StatusText.Text = $"загружено: {UserProfile.Nick}";
}

private void Back_Click(object sender, RoutedEventArgs e)
{
BackRequested?.Invoke();
}

private void Save_Click(object sender, RoutedEventArgs e)
{
try
{
var nick = (TxtUsername.Text ?? "").Trim();
var desc = (TxtDescription.Text ?? "").Trim();

if (string.IsNullOrWhiteSpace(nick))
{
StatusText.Text = "имя не может быть пустым";
return;
}

UserProfile.Save(nick, desc);
StatusText.Text = $"сохранено {DateTime.Now:HH:mm:ss}";
GuiTestLogger.Log("SETTINGS_SAVE", nick, desc, true);
}
catch (Exception ex)
{
StatusText.Text = "ошибка: " + ex.Message;
GuiTestLogger.Log("SETTINGS_SAVE", "", ex.Message, false);
}
}

// ── Тесты инструментов (перенесены из старого HubSettingsWindow) ──────

private void RunTest(string name, Action action)
{
try
{
action();
GuiTestLogger.Log(name, "", "Успешно", true);
StatusText.Text = $"[{DateTime.Now:HH:mm:ss}] {name} → ОК";
}
catch (Exception ex)
{
GuiTestLogger.Log(name, "", ex.Message, false);
StatusText.Text = $"[{DateTime.Now:HH:mm:ss}] {name} → ОШИБКА: {ex.Message}";
}
}

private void TestReadExact_Click(object sender, RoutedEventArgs e)
{
RunTest("file_read_exact", () =>
{
var temp = Path.GetTempFileName();
File.WriteAllText(temp, "Line1\nLine2\nLine3");
var res = GatewayState.FileReadExact(temp, 2, 3);
if (!res.Contains("2: Line2")) throw new Exception("Неверный результат");
File.Delete(temp);
});
}

private void TestWriteLines_Click(object sender, RoutedEventArgs e)
{
RunTest("file_write_lines", () =>
{
var temp = Path.GetTempFileName();
File.WriteAllText(temp, "A\nB\nC");
GatewayState.FileWriteLines(temp, 2, 2, "X\nY");
var res = File.ReadAllText(temp);
if (!res.Contains("X") || !res.Contains("C")) throw new Exception("Неверная замена");
File.Delete(temp);
});
}

private void TestInsert_Click(object sender, RoutedEventArgs e)
{
RunTest("file_insert", () =>
{
var temp = Path.GetTempFileName();
File.WriteAllText(temp, "1\n2");
GatewayState.FileInsert(temp, 2, "INSERTED");
var res = File.ReadAllText(temp);
if (!res.Contains("INSERTED")) throw new Exception("Вставка не сработала");
File.Delete(temp);
});
}

private void TestWriteFull_Click(object sender, RoutedEventArgs e)
{
RunTest("file_write_full", () =>
{
var temp = Path.GetTempFileName();
GatewayState.FileWriteFull(temp, "FULL");
if (File.ReadAllText(temp) != "FULL") throw new Exception("Полная перезапись не сработала");
File.Delete(temp);
});
}

private void TestFullGui_Click(object sender, RoutedEventArgs e)
{
RunTest("Full_GUI", () =>
{
GuiTestLogger.Log("GUI_OPEN", "HubSettingsPage", "OK", true);
});
}
}
}