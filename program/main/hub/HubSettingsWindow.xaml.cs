using System;
using MainApp;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Gateway;
namespace Hub {
public partial class HubSettingsWindow : Window {
public HubSettingsWindow() {
InitializeComponent();
try {
var path = BrowserLauncher.GetConfigPath();
if (path != null && File.Exists(path)) {
var node = JsonNode.Parse(File.ReadAllText(path));
var hs = node?["HubSettings"];
TxtUsername.Text = hs?["Username"]?.GetValue<string>() ?? "";
TxtDescription.Text = hs?["Description"]?.GetValue<string>() ?? "";
}
} catch { }
}
private void Save_Click(object sender, RoutedEventArgs e) {
try {
var path = BrowserLauncher.GetConfigPath();
if (path == null) throw new InvalidOperationException("Не найден путь к config.json");
JsonObject node;
try { node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject(); }
catch { node = new JsonObject(); }
node["HubSettings"] = new JsonObject {
["Username"] = TxtUsername.Text,
["Description"] = TxtDescription.Text
};
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
MessageBox.Show("Настройки сохранены!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
Close();
} catch (Exception ex) {
MessageBox.Show("Ошибка сохранения: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
}
}
private void RunTest(string name, Action action) {
try {
action();
GuiTestLogger.Log(name, "", "Успешно", true);
MessageBox.Show($"Тест {name} пройден!", "Тест", MessageBoxButton.OK, MessageBoxImage.Information);
} catch (Exception ex) {
GuiTestLogger.Log(name, "", ex.Message, false);
MessageBox.Show($"Ошибка в тесте {name}: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
}
}
private void TestReadExact_Click(object sender, RoutedEventArgs e) {
RunTest("file_read_exact", () => {
var temp = Path.GetTempFileName();
File.WriteAllText(temp, "Line1\nLine2\nLine3");
var res = MainApp.GatewayState.FileReadExact(temp, 2, 3);
if (!res.Contains("2: Line2")) throw new Exception("Неверный результат");
File.Delete(temp);
});
}
private void TestWriteLines_Click(object sender, RoutedEventArgs e) {
RunTest("file_write_lines", () => {
var temp = Path.GetTempFileName();
File.WriteAllText(temp, "A\nB\nC");
MainApp.GatewayState.FileWriteLines(temp, 2, 2, "X\nY");
var res = File.ReadAllText(temp);
if (!res.Contains("X") || !res.Contains("C")) throw new Exception("Неверная замена");
File.Delete(temp);
});
}
private void TestInsert_Click(object sender, RoutedEventArgs e) {
RunTest("file_insert", () => {
var temp = Path.GetTempFileName();
File.WriteAllText(temp, "1\n2");
MainApp.GatewayState.FileInsert(temp, 2, "INSERTED");
var res = File.ReadAllText(temp);
if (!res.Contains("INSERTED")) throw new Exception("Вставка не сработала");
File.Delete(temp);
});
}
private void TestWriteFull_Click(object sender, RoutedEventArgs e) {
RunTest("file_write_full", () => {
var temp = Path.GetTempFileName();
MainApp.GatewayState.FileWriteFull(temp, "FULL");
if (File.ReadAllText(temp) != "FULL") throw new Exception("Полная перезапись не сработала");
File.Delete(temp);
});
}
private void TestFullGui_Click(object sender, RoutedEventArgs e) {
RunTest("Full_GUI", () => {
GuiTestLogger.Log("GUI_OPEN", "HubSettingsWindow", "OK", true);
});
}
}
}