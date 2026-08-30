using System;
using System.IO;
using System.Text;
namespace Gateway {
public static class GuiTestLogger {
private static readonly object _lock = new object();
private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "logs", $"gui_test_{DateTime.Now:yyyyMMdd}.log");
public static void Log(string action, string input, string result, bool success) {
var dir = Path.GetDirectoryName(LogPath);
if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
var msg = $"[{DateTime.Now:HH:mm:ss}] {action} | {input} | {result} | {(success ? "ОК" : "ОШИБКА")}\n";
lock (_lock) { File.AppendAllText(LogPath, msg, Encoding.UTF8); }
}
}
public static class AiExchangeLogger {
private static readonly object _lock = new object();
private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "logs", $"ai_exchange_{DateTime.Now:yyyyMMdd}.log");
public static void Log(string role, string text) {
var dir = Path.GetDirectoryName(LogPath);
if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
var msg = $"[{DateTime.Now:HH:mm:ss}] {role}\n{text}\n---\n";
lock (_lock) { File.AppendAllText(LogPath, msg, Encoding.UTF8); }
}
}
}