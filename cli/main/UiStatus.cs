// UiStatus.cs — статус-панели: DrawStatus, DrawDispatcherStatus, WriteStatusLine
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.IO;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  STATUS (общий)
    // ══════════════════════════════════════════════════════════
    static void DrawStatus()
    {
        lock (PrintLock)
        {
            int winW;

            try { winW = Console.WindowWidth; }
            catch { winW = 80; }

            if (winW < 40)
                winW = 40;

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭─ ▸ СТАТУС " + new string('─', Math.Max(1, winW - 16)) + "╮");
            Console.ResetColor();

            int histCount;

            lock (HistoryLock)
            {
                histCount = History.Count;
            }

            WriteStatusLine(
                "Chat ID",
                string.IsNullOrEmpty(ChatId) ? "—" : ChatId,
                !string.IsNullOrEmpty(ChatId));

            WriteStatusLine(
                "Token",
                string.IsNullOrEmpty(Token)
                    ? "отсутствует"
                    : Token.Substring(0, Math.Min(8, Token.Length)) + "...",
                !string.IsNullOrEmpty(Token));

            WriteStatusLine("API", ApiBaseUrl, true);
            WriteStatusLine("Primary", PrimaryModel, true);

            WriteStatusLine(
                "Config",
                File.Exists(ConfigFile) ? "найден" : "НЕ найден",
                File.Exists(ConfigFile));

            WriteStatusLine("Версия", "v" + AppVersion, true);
            WriteStatusLine("Папка", BaseDir, true);

            WriteStatusLine(
                "Live",
                IsLiveRunning() ? "активен" : "остановлен",
                IsLiveRunning());

            WriteStatusLine("Think", ShowThinking ? "ON" : "OFF", true);
            WriteStatusLine("Anim", AnimationsEnabled ? "ON" : "OFF", true);

            // ── Dispatcher v6.0 ──
            WriteStatusLine(
                "Dispatcher",
                DispatcherEnabled ? "ON" : "OFF",
                DispatcherEnabled);

            WriteStatusLine(
                "Compress",
                CompressEnabled ? "ON" : "OFF",
                CompressEnabled);

            WriteStatusLine(
                "Extract",
                ExtractEnabled ? "ON" : "OFF",
                ExtractEnabled);

            WriteStatusLine(
                "AI2 Model",
                GetAi2Model(),
                true);

            WriteStatusLine(
                "AI2 Token",
                string.IsNullOrEmpty(Token2)
                    ? "НЕТ"
                    : Token2.Substring(0, Math.Min(8, Token2.Length)) + "...",
                !string.IsNullOrEmpty(Token2));

            WriteStatusLine(
                "AI2 Chat",
                string.IsNullOrEmpty(ChatId2) ? "НЕТ" : ChatId2,
                !string.IsNullOrEmpty(ChatId2));

            WriteStatusLine(
                "ArcMode",
                ArcMode ? "ON (авто)" : "OFF",
                ArcMode);

            int rollbackCount;

            lock (RollbackHistory)
            {
                rollbackCount = RollbackHistory.Count;
            }

            WriteStatusLine(
                "Rollback",
                rollbackCount + "/" + MaxRollbackEntries,
                rollbackCount > 0);

            int logCount;

            lock (ChangeLog)
            {
                logCount = ChangeLog.Count;
            }

            WriteStatusLine(
                "Log",
                logCount + " записей",
                logCount > 0);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  │  " + "История".PadRight(10));

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(histCount + "/" + MaxHistoryEntries + " ");

            int barW = 15;

            int filled = MaxHistoryEntries > 0
                ? (int)((double)histCount / MaxHistoryEntries * barW)
                : 0;

            if (filled > barW)
                filled = barW;

            if (filled < 0)
                filled = 0;

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("[");

            Console.ForegroundColor = histCount > MaxHistoryEntries * 0.8
                ? ConsoleColor.Yellow
                : ConsoleColor.Green;

            for (int i = 0; i < filled; i++)
                Console.Write("█");

            Console.ForegroundColor = ConsoleColor.DarkGray;

            for (int i = filled; i < barW; i++)
                Console.Write("░");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("]");

            Console.ResetColor();

            WriteStatusLine(
                "Parent",
                string.IsNullOrEmpty(LastResponseId)
                    ? "null"
                    : LastResponseId.Substring(0, Math.Min(8, LastResponseId.Length)) + "...",
                !string.IsNullOrEmpty(LastResponseId));

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");

            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  DISPATCHER STATUS (v6.0)
    // ══════════════════════════════════════════════════════════
    static void DrawDispatcherStatus()
    {
        lock (PrintLock)
        {
            int winW;

            try { winW = Console.WindowWidth; }
            catch { winW = 80; }

            if (winW < 40)
                winW = 40;

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  ╭─ ▸ ◆ DISPATCHER " + new string('─', Math.Max(1, winW - 22)) + "╮");
            Console.ResetColor();

            WriteStatusLine(
                "Dispatcher",
                DispatcherEnabled ? "ON" : "OFF",
                DispatcherEnabled);

            WriteStatusLine(
                "Compress",
                CompressEnabled ? "ON" : "OFF",
                CompressEnabled);

            WriteStatusLine(
                "Extract",
                ExtractEnabled ? "ON" : "OFF",
                ExtractEnabled);

            WriteStatusLine(
                "Validate",
                Ai2ValidateEnabled ? "ON" : "OFF",
                Ai2ValidateEnabled);

            WriteStatusLine(
                "AI #2 Model",
                string.IsNullOrEmpty(Ai2Model) ? DefaultAi2Model : Ai2Model,
                true);

            WriteStatusLine(
                "AI #2 Token",
                string.IsNullOrEmpty(Token2)
                    ? "НЕТ"
                    : Token2.Substring(0, Math.Min(8, Token2.Length)) + "...",
                !string.IsNullOrEmpty(Token2));

            WriteStatusLine(
                "AI #2 Chat",
                string.IsNullOrEmpty(ChatId2) ? "НЕТ" : ChatId2,
                !string.IsNullOrEmpty(ChatId2));

            WriteStatusLine(
                "AI #2 API",
                (string.IsNullOrEmpty(ApiBaseUrl2) || ApiBaseUrl2 == DefaultApiBase)
                    ? "(AI #1)"
                    : ApiBaseUrl2,
                !string.IsNullOrEmpty(ApiBaseUrl2) && ApiBaseUrl2 != DefaultApiBase);

            WriteStatusLine(
                "Project",
                string.IsNullOrEmpty(ProjectPath) ? "(auto)" : ProjectPath,
                !string.IsNullOrEmpty(ProjectPath));

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");

            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  WRITE STATUS LINE (helper)
    // ══════════════════════════════════════════════════════════
    static void WriteStatusLine(string label, string value, bool ok)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  │  " + (label ?? "").PadRight(10));

        Console.ForegroundColor = ok ? ConsoleColor.Gray : ConsoleColor.Red;
        Console.WriteLine(value ?? "");

        Console.ResetColor();
    }
}