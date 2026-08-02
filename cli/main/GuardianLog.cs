// GuardianLog.cs — rollback-снимки, лог изменений, валидация через Guardian, компрессия
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>Снимок файла для rollback</summary>
class RollbackEntry
{
    public string FilePath;
    public string Content;
    public string Timestamp;
}

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  GUARDIAN CONSTANTS
    // ══════════════════════════════════════════════════════════
    const int GuardianMaxRetries  = 3;
    const int MaxRollbackEntries  = 50;
    const int MaxChangeLogEntries = 200;

    static readonly List<RollbackEntry> RollbackHistory = new List<RollbackEntry>();
    static readonly List<string> GuardianChangeLog = new List<string>();

    // ══════════════════════════════════════════════════════════
    //  ROLLBACK SNAPSHOT
    // ══════════════════════════════════════════════════════════
    static void SaveRollbackSnapshot(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
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
        catch { }
    }

    // ══════════════════════════════════════════════════════════
    //  GUARDIAN CHANGE LOG (с компрессией)
    // ══════════════════════════════════════════════════════════
    static void GuardianLog(string file, string action, string status)
    {
        string entry = "[" + DateTime.Now.ToString("dd.MM HH:mm:ss") + "] " +
                       "FILE: " + (file ?? "unknown") + " | " +
                       "ACTION: " + (action ?? "MODIFY") + " | " +
                       "STATUS: " + (status ?? "unknown");
        lock (GuardianChangeLog)
        {
            GuardianChangeLog.Add(entry);
            // Компрессия: при превышении лимита суммируем старую половину
            if (GuardianChangeLog.Count > MaxChangeLogEntries)
            {
                int half = GuardianChangeLog.Count / 2;
                string summary = "[COMPRESSED] " + half + " старых записей (" +
                                 GuardianChangeLog[0] + " ... " +
                                 GuardianChangeLog[half - 1] + ")";
                GuardianChangeLog.RemoveRange(0, half);
                GuardianChangeLog.Insert(0, summary);
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  VALIDATE FILE CONTENT WITH GUARDIAN
    //  Отправляет содержимое файла + контекст плана в Guardian,
    //  получает PASS / FAIL.
    // ══════════════════════════════════════════════════════════
    static bool ValidateFileContentWithGuardian(string filePath, string content, string planContext)
    {
        if (!GuardianEnabled) return true;
        if (string.IsNullOrEmpty(content)) return true;

        try
        {
            string fileName = Path.GetFileName(filePath ?? "unknown");
            string truncated = content;
            if (truncated.Length > 8000)
                truncated = truncated.Substring(0, 8000) + "\n... [truncated]";

            string userPrompt =
                "Validate this proposed file content.\n" +
                "File: " + fileName + "\n" +
                "Plan context:\n" + (planContext ?? "none") + "\n\n" +
                "Proposed content:\n" + truncated + "\n\n" +
                "Check: syntax, logic, completeness, adherence to plan. " +
                "Respond PASS or FAIL with specific errors.";

            string response = PostGuardianMessage(GuardianValidationPrompt, userPrompt);
            return IsGuardianPass(response);
        }
        catch
        {
            // При ошибке Guardian — не блокируем (graceful bypass)
            return true;
        }
    }
}