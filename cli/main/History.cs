// History.cs — локальная история чата
// New Era v7.2
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
class HistoryEntry
{
public string Role;
public string Time;
public string Text;
}
partial class MainConsole
{
static void AddHistory(string role, string text)
{
if (string.IsNullOrEmpty(text)) return;
lock (HistoryLock) {
History.Add(new HistoryEntry {
Role = role ?? "unknown",
Time = DateTime.Now.ToString("dd.MM HH:mm"),
Text = text
});
while (History.Count > MaxHistoryEntries) History.RemoveAt(0);
}
}
static void LoadHistory()
{
    if (!File.Exists(HistoryFile)) return;
    for (int attempt = 0; attempt < 3; attempt++) {
        try {
            string content = ReadTextAuto(HistoryFile);
            string[] blocks = content.Split(new[] { "<<" }, StringSplitOptions.None);
            lock (HistoryLock) {
                History.Clear();
                foreach (string block in blocks) {
                    if (!block.StartsWith("MSG")) continue;
                    int headerEnd = block.IndexOf('\n');
                    if (headerEnd < 0) continue;
                    string header = block.Substring(0, headerEnd).Trim();
                    string role = "", time = "";
                    foreach (string hp in header.Split(' ')) {
                        if (hp.StartsWith("role=")) role = hp.Substring(5);
                        else if (hp.StartsWith("time=")) time = hp.Substring(5);
                    }
                    int endMarker = block.IndexOf("MSG>>");
                    string text = endMarker >= 0
                        ? block.Substring(headerEnd + 1, endMarker - headerEnd - 1).TrimEnd('\r', '\n')
                        : block.Substring(headerEnd + 1).TrimEnd('\r', '\n');
                    if (role.Length > 0)
                        History.Add(new HistoryEntry { Role = role, Time = time, Text = text });
                }
                while (History.Count > MaxHistoryEntries) History.RemoveAt(0);
            }
            return;
        } catch (IOException) {
            Thread.Sleep(200 * (attempt + 1));
        } catch {
            return;
        }
    }
}

static void SaveHistory()
{
    try {
        lock (HistoryLock) {
            var sb = new StringBuilder();
            foreach (var entry in History) {
                sb.AppendLine("<<MSG role=" + (entry.Role ?? "unknown") + " time=" + (entry.Time ?? ""));
                sb.AppendLine(entry.Text ?? "");
                sb.AppendLine("MSG>>");
            }
            File.WriteAllText(HistoryFile, sb.ToString(), new UTF8Encoding(false));
        }
    } catch { }
}

static void ShowHistory()
{
    List<HistoryEntry> snapshot;
    int count;
    lock (HistoryLock) {
        count = History.Count;
        snapshot = new List<HistoryEntry>(History);
    }
    if (count == 0) {
        WriteColored(ConsoleColor.DarkGray, "  \u25CC История пуста.\n");
        return;
    }
    lock (PrintLock) {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u256D\u2500 \u25B8 ИСТОРИЯ (" + count + ") " + new string('\u2500', 25) + "\u256E");
        Console.ResetColor();
        int showCount = Math.Min(count, 20);
        int startIdx = count - showCount;
        for (int i = startIdx; i < count; i++) {
            var e = snapshot[i];
            Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u2502 ");
            Console.ForegroundColor = e.Role == "user" ? ConsoleColor.Green : ConsoleColor.Magenta;
            Console.Write(e.Role == "user" ? "\u276F " : "\u25C6 ");
            Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write((e.Time ?? "") + "  ");
            Console.ForegroundColor = ConsoleColor.White;
            string preview = (e.Text ?? "").Replace("\n", " ").Replace("\r", "");
            if (preview.Length > 60) preview = preview.Substring(0, 57) + "...";
            Console.WriteLine(preview);
        }
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F");
        Console.ResetColor();
        Console.WriteLine();
    }
}

static void ClearHistory()
{
    lock (HistoryLock) { History.Clear(); }
    try { if (File.Exists(HistoryFile)) File.Delete(HistoryFile); } catch { }
    WriteColored(ConsoleColor.Green, "  \u2714 История очищена.\n");
}
}