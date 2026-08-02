// ChangeLog.cs — rollback-снимки и лог изменений
// New Era CLI v6.0
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>Снимок файла для rollback.</summary>
class RollbackEntry
{
    public string FilePath;
    public string Content;
    public string Timestamp;
}

partial class MainConsole
{
    const int MaxRollbackEntries  = 50;
    const int MaxChangeLogEntries = 200;

    static readonly List<RollbackEntry> RollbackHistory = new List<RollbackEntry>();
    static readonly List<string> ChangeLog = new List<string>();

    // ══════════════════════════════════════════════════════════
    //  ROLLBACK SNAPSHOT
    // ══════════════════════════════════════════════════════════
    static void SaveRollbackSnapshot(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        try
        {
            string content = File.Exists(filePath) ? ReadTextAuto(filePath) : null;

            lock (RollbackHistory)
            {
                RollbackHistory.Add(new RollbackEntry
                {
                    FilePath  = filePath,
                    Content   = content,
                    Timestamp = DateTime.Now.ToString("dd.MM HH:mm:ss")
                });

                while (RollbackHistory.Count > MaxRollbackEntries)
                    RollbackHistory.RemoveAt(0);
            }
        }
        catch
        {
        }
    }

    // ══════════════════════════════════════════════════════════
    //  CHANGE LOG
    // ══════════════════════════════════════════════════════════
    static void LogChange(string file, string action, string status)
    {
        string entry =
            "[" + DateTime.Now.ToString("dd.MM HH:mm:ss") + "] " +
            "FILE: " + (file ?? "unknown") + " | " +
            "ACTION: " + (action ?? "MODIFY") + " | " +
            "STATUS: " + (status ?? "unknown");

        lock (ChangeLog)
        {
            ChangeLog.Add(entry);

            if (ChangeLog.Count > MaxChangeLogEntries)
            {
                int half = ChangeLog.Count / 2;

                string summary =
                    "[COMPRESSED] " + half + " старых записей (" +
                    ChangeLog[0] + " ... " +
                    ChangeLog[half - 1] + ")";

                ChangeLog.RemoveRange(0, half);
                ChangeLog.Insert(0, summary);
            }
        }
    }
}