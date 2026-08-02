// UiStatus.cs — статус-панели: DrawStatus, DrawOrchestratorStatus, DrawGuardianStatus, WriteStatusLine
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.IO;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  ORCHESTRATOR STATUS
    // ══════════════════════════════════════════════════════════
    static void DrawOrchestratorStatus()
    {
        lock (PrintLock)
        {
            int winW;
            try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 40) winW = 40;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭─ ▸ ОРКЕСТРАТОР " + new string('─', Math.Max(1, winW - 22)) + "╮");
            Console.ResetColor();

            WriteStatusLine("Enabled", OrchestratorEnabled ? "ON" : "OFF", OrchestratorEnabled);
            WriteStatusLine("Model", string.IsNullOrEmpty(OrchestratorModel) ? "(primary)" : OrchestratorModel, !string.IsNullOrEmpty(OrchestratorModel));
            WriteStatusLine("API", string.IsNullOrEmpty(OrchestratorApiUrl) ? "(primary)" : OrchestratorApiUrl, !string.IsNullOrEmpty(OrchestratorApiUrl));
            WriteStatusLine("Token", string.IsNullOrEmpty(OrchestratorToken) ? "(primary)" : OrchestratorToken.Substring(0, Math.Min(8, OrchestratorToken.Length)) + "...", !string.IsNullOrEmpty(OrchestratorToken));
            WriteStatusLine("Primary", PrimaryModel, true);

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  GUARDIAN STATUS (SYSTEM_GUARDIAN + ArcMode)
    // ══════════════════════════════════════════════════════════
    static void DrawGuardianStatus()
    {
        lock (PrintLock)
        {
            int winW;
            try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 40) winW = 40;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  ╭─ ▸ ◆ GUARDIAN " + new string('─', Math.Max(1, winW - 20)) + "╮");
            Console.ResetColor();

            WriteStatusLine("Guardian", GuardianEnabled ? "ON" : "OFF", GuardianEnabled);
            WriteStatusLine("ArcMode", ArcMode ? "ON (авто)" : "OFF", ArcMode);
            WriteStatusLine("Model", string.IsNullOrEmpty(GuardianModel) ? "(primary)" : GuardianModel, !string.IsNullOrEmpty(GuardianModel));
            WriteStatusLine("API", string.IsNullOrEmpty(GuardianApiUrl) ? "(primary)" : GuardianApiUrl, !string.IsNullOrEmpty(GuardianApiUrl));
            WriteStatusLine("Token", string.IsNullOrEmpty(GuardianToken) ? "(primary)" : GuardianToken.Substring(0, Math.Min(8, GuardianToken.Length)) + "...", !string.IsNullOrEmpty(GuardianToken));
            WriteStatusLine("Primary", PrimaryModel, true);

            int rollbackCount;
            lock (RollbackHistory) { rollbackCount = RollbackHistory.Count; }
            WriteStatusLine("Rollback", rollbackCount + "/" + MaxRollbackEntries, rollbackCount > 0);

            int logCount;
            lock (GuardianChangeLog) { logCount = GuardianChangeLog.Count; }
            WriteStatusLine("Log", logCount + " записей", logCount > 0);

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  STATUS (общий)
    // ══════════════════════════════════════════════════════════
    static void DrawStatus()
    {
        lock (PrintLock)
        {
            int winW;
            try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 40) winW = 40;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭─ ▸ СТАТУС " + new string('─', Math.Max(1, winW - 16)) + "╮");
            Console.ResetColor();

            int histCount;
            lock (HistoryLock) { histCount = History.Count; }

            WriteStatusLine("Chat ID", string.IsNullOrEmpty(ChatId) ? "—" : ChatId, !string.IsNullOrEmpty(ChatId));
            WriteStatusLine("Token", string.IsNullOrEmpty(Token) ? "отсутствует" : Token.Substring(0, Math.Min(8, Token.Length)) + "...", !string.IsNullOrEmpty(Token));
            WriteStatusLine("API", ApiBaseUrl, true);
            WriteStatusLine("Model", PrimaryModel, true);
            WriteStatusLine("Config", File.Exists(ConfigFile) ? "найден" : "НЕ найден", File.Exists(ConfigFile));
            WriteStatusLine("Версия", "v" + AppVersion, true);
            WriteStatusLine("Папка", BaseDir, true);
            WriteStatusLine("Live", IsLiveRunning() ? "активен" : "остановлен", IsLiveRunning());
            WriteStatusLine("Think", ShowThinking ? "ON" : "OFF", true);
            WriteStatusLine("Anim", AnimationsEnabled ? "ON" : "OFF", true);

            // ── Orchestrator (Dual-LLM) ──
            WriteStatusLine("Orch", OrchestratorEnabled ? "ON" : "OFF", OrchestratorEnabled);
            WriteStatusLine("Orch Model", string.IsNullOrEmpty(OrchestratorModel) ? "(primary)" : OrchestratorModel, !string.IsNullOrEmpty(OrchestratorModel));
            WriteStatusLine("Orch API", string.IsNullOrEmpty(OrchestratorApiUrl) ? "(primary)" : OrchestratorApiUrl, !string.IsNullOrEmpty(OrchestratorApiUrl));

            // ── SYSTEM_GUARDIAN ──
            WriteStatusLine("Guardian", GuardianEnabled ? "ON" : "OFF", GuardianEnabled);
            WriteStatusLine("ArcMode", ArcMode ? "ON (авто)" : "OFF", ArcMode);
            WriteStatusLine("Grd Model", string.IsNullOrEmpty(GuardianModel) ? "(primary)" : GuardianModel, !string.IsNullOrEmpty(GuardianModel));
            WriteStatusLine("Grd API", string.IsNullOrEmpty(GuardianApiUrl) ? "(primary)" : GuardianApiUrl, !string.IsNullOrEmpty(GuardianApiUrl));

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  │  " + "История".PadRight(10));
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(histCount + "/" + MaxHistoryEntries + " ");

            int barW = 15;
            int filled = MaxHistoryEntries > 0 ? (int)((double)histCount / MaxHistoryEntries * barW) : 0;
            if (filled > barW) filled = barW;
            if (filled < 0) filled = 0;

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("[");
            Console.ForegroundColor = histCount > MaxHistoryEntries * 0.8 ? ConsoleColor.Yellow : ConsoleColor.Green;
            for (int i = 0; i < filled; i++) Console.Write("█");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            for (int i = filled; i < barW; i++) Console.Write("░");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("]");
            Console.ResetColor();

            WriteStatusLine("Parent", string.IsNullOrEmpty(LastResponseId) ? "null" : LastResponseId.Substring(0, Math.Min(8, LastResponseId.Length)) + "...", !string.IsNullOrEmpty(LastResponseId));

            Console.ForegroundColor = ConsoleColor.DarkCyan;
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

// ══════════════════════════════════════════════════════════
//  DISPATCHER STATUS (v6.0)
// ══════════════════════════════════════════════════════════
static void DrawDispatcherStatus()
{
    lock (PrintLock)
    {
        int winW;
        try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 40) winW = 40;
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("  ╭─ ▸ ◆ DISPATCHER " + new string('─', Math.Max(1, winW - 22)) + "╮");
        Console.ResetColor();
        WriteStatusLine("Dispatcher", DispatcherEnabled ? "ON" : "OFF", DispatcherEnabled);
        WriteStatusLine("Compress", CompressEnabled ? "ON" : "OFF", CompressEnabled);
        WriteStatusLine("Extract", ExtractEnabled ? "ON" : "OFF", ExtractEnabled);
        WriteStatusLine("AI #2 Model", string.IsNullOrEmpty(Ai2Model) ? "(primary)" : Ai2Model, !string.IsNullOrEmpty(Ai2Model));
        WriteStatusLine("AI #2 Token", string.IsNullOrEmpty(Token2) ? "НЕТ" : Token2.Substring(0, Math.Min(8, Token2.Length)) + "...", !string.IsNullOrEmpty(Token2));
        WriteStatusLine("AI #2 Chat", string.IsNullOrEmpty(ChatId2) ? "(AI #1)" : ChatId2, !string.IsNullOrEmpty(ChatId2));
        WriteStatusLine("AI #2 API", (string.IsNullOrEmpty(ApiBaseUrl2) || ApiBaseUrl2 == DefaultApiBase) ? "(AI #1)" : ApiBaseUrl2, !string.IsNullOrEmpty(ApiBaseUrl2) && ApiBaseUrl2 != DefaultApiBase);
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");
        Console.ResetColor();
        Console.WriteLine();
    }
}

}