// History.cs — локальная история чата
// New Era CLI v4.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

class HistoryEntry
{
    public string Role;
    public string Time;
    public string Text;
}

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  HISTORY
    // ══════════════════════════════════════════════════════════

    static void AddHistory(string role, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (HistoryLock)
        {
            History.Add(new HistoryEntry
            {
                Role = role ?? "unknown",
                Time = DateTime.Now.ToString("dd.MM HH:mm"),
                Text = text
            });

            while (History.Count > MaxHistoryEntries)
                History.RemoveAt(0);
        }
    }

    static void LoadHistory()
    {
        if (!File.Exists(HistoryFile)) return;

        try
        {
            string content = ReadTextAuto(HistoryFile);
            string[] blocks = content.Split(new[] { "<<" }, StringSplitOptions.None);

            lock (HistoryLock)
            {
                History.Clear();

                foreach (string block in blocks)
                {
                    if (!block.StartsWith("MSG")) continue;

                    int headerEnd = block.IndexOf('\n');
                    if (headerEnd < 0) continue;

                    string header = block.Substring(0, headerEnd).Trim();
                    string role = "";
                    string time = "";

                    string[] hparts = header.Split(' ');
                    foreach (string hp in hparts)
                    {
                        if (hp.StartsWith("role=")) role = hp.Substring(5);
                        else if (hp.StartsWith("time=")) time = hp.Substring(5);
                    }

                    int endMarker = block.IndexOf("MSG>>");
                    string text;
                    if (endMarker >= 0)
                        text = block.Substring(headerEnd + 1, endMarker - headerEnd - 1).TrimEnd('\r', '\n');
                    else
                        text = block.Substring(headerEnd + 1).TrimEnd('\r', '\n');

                    if (role.Length > 0)
                        History.Add(new HistoryEntry { Role = role, Time = time, Text = text });
                }

                while (History.Count > MaxHistoryEntries)
                    History.RemoveAt(0);
            }
        }
        catch { }
    }

    static void SaveHistory()
    {
        try
        {
            lock (HistoryLock)
            {
                var sb = new StringBuilder();
                foreach (var entry in History)
                {
                    sb.AppendLine("<<MSG role=" + (entry.Role ?? "unknown") + " time=" + (entry.Time ?? ""));
                    sb.AppendLine(entry.Text ?? "");
                    sb.AppendLine("MSG>>");
                }
                File.WriteAllText(HistoryFile, sb.ToString(), new UTF8Encoding(false));
            }
        }
        catch { }
    }

    static void ShowHistory()
    {
        lock (HistoryLock)
        {
            if (History.Count == 0)
            {
                WriteColored(ConsoleColor.DarkGray, "  \u25CC \u0418\u0441\u0442\u043E\u0440\u0438\u044F \u043F\u0443\u0441\u0442\u0430.\n");
                return;
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u256D\u2500 \u25B8 \u0418\u0421\u0422\u041E\u0420\u0418\u042F (" + History.Count + ") " + new string('\u2500', 30) + "\u256E");
            Console.ResetColor();

            int showCount = Math.Min(History.Count, 20);
            int startIdx = History.Count - showCount;

            for (int i = startIdx; i < History.Count; i++)
            {
                var e = History[i];
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("  \u2502 ");
                Console.ForegroundColor = e.Role == "user" ? ConsoleColor.Green : ConsoleColor.Magenta;
                Console.Write(e.Role == "user" ? "\u276F " : "\u25C6 ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write((e.Time ?? "") + "  ");
                Console.ForegroundColor = ConsoleColor.White;

                string preview = (e.Text ?? "").Replace("\n", " ").Replace("\r", "");
                if (preview.Length > 60) preview = preview.Substring(0, 57) + "...";
                Console.WriteLine(preview);
            }

            if (History.Count > showCount)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  \u2502  ... \u0435\u0449\u0451 " + (History.Count - showCount) + " \u0441\u043E\u043E\u0431\u0449\u0435\u043D\u0438\u0439");
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u2570" + new string('\u2500', 46) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void ClearHistory()
    {
        lock (HistoryLock) { History.Clear(); }
        try { if (File.Exists(HistoryFile)) File.Delete(HistoryFile); } catch { }
        WriteColored(ConsoleColor.Green, "  \u2714 \u0418\u0441\u0442\u043E\u0440\u0438\u044F \u043E\u0447\u0438\u0449\u0435\u043D\u0430.\n");
    }
}