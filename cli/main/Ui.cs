// Ui.cs — баннер, промпт, help, WriteColored
// New Era CLI v5.3 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// v5.3:
//   - В help добавлены команды /test.

using System;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  BANNER
    // ══════════════════════════════════════════════════════════
    static void DrawBanner()
    {
        lock (PrintLock)
        {
            int winW;

            try { winW = Console.WindowWidth; } catch { winW = 80; }

            if (winW < 40) winW = 40;

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭" + new string('─', winW - 4) + "╮");

            Console.Write("  │  ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("███");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  ");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("NEW ERA");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  v" + AppVersion);

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  ·  Qwen CLI  ·  ");

            Console.ForegroundColor = string.IsNullOrEmpty(ChatId) ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.Write(string.IsNullOrEmpty(ChatId) ? "● offline" : "● online");

            // Dual-LLM индикатор
            if (OrchestratorEnabled)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("  ");

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("◆ dual-llm");
            }

            // Guardian индикатор
            if (GuardianEnabled)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("  ");

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("◆ guardian");
            }

            // Аркест индикатор
            if (ArcMode)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("  ");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("◆ arc");
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;

            int pad = winW - 4 - 2 - 3 - 3 - 7 - 4 - 18
                      - (string.IsNullOrEmpty(ChatId) ? 9 : 8)
                      - (OrchestratorEnabled ? 12 : 0)
                      - (GuardianEnabled ? 12 : 0)
                      - (ArcMode ? 7 : 0);

            if (pad < 1) pad = 1;

            Console.Write(new string(' ', pad));
            Console.WriteLine("│");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ├" + new string('─', winW - 4) + "┤");

            Console.ResetColor();

            if (string.IsNullOrEmpty(ChatId))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  │  ⚠ Заполни qwen_config.txt (CHAT_ID, TOKEN)");

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  │    " + ConfigFile);

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("  ├" + new string('─', winW - 4) + "┤");
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  │  ▸ /help — команды   ▸ /exit — выход   ▸ /status — статус");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");

            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PROMPT
    // ══════════════════════════════════════════════════════════
    static void DrawPrompt()
    {
        lock (PrintLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  ");

            Console.ForegroundColor = string.IsNullOrEmpty(Token) ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.Write("❯");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ");

            Console.ResetColor();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  HELP
    // ══════════════════════════════════════════════════════════
    static void DrawHelp()
    {
        lock (PrintLock)
        {
            int winW;

            try { winW = Console.WindowWidth; } catch { winW = 80; }

            if (winW < 40) winW = 40;

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭─ ▸ КОМАНДЫ " + new string('─', Math.Max(1, winW - 18)) + "╮");

            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── Чат ──");
            Console.ResetColor();

            WriteHelpLine("<текст>", "сообщение → ответ ИИ");
            WriteHelpLine("/say <т>", "то же самое");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── Код ──");
            Console.ResetColor();

            WriteHelpLine("/edit <файл> <з>", "ИИ правит файл");
            WriteHelpLine("/edit <ф> N-M <з>", "править строки N-M");
            WriteHelpLine("/edit <папка> <з>", "создать файл(ы)");
            WriteHelpLine("/plan <путь> <з>", "план реализации");
            WriteHelpLine("/scan <папка>", "отчёт по структуре");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── История ──");
            Console.ResetColor();

            WriteHelpLine("/history", "локальная история");
            WriteHelpLine("/history clear", "очистить историю");
            WriteHelpLine("/fetch", "история с сервера");
            WriteHelpLine("/live  /tail", "слежение за чатом");
            WriteHelpLine("/stop", "остановить live");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── Оркестратор ──");
            Console.ResetColor();

            WriteHelpLine("/orch on|off", "вкл/выкл dual-LLM");
            WriteHelpLine("/orch status", "статус оркестратора");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  │  ── Тест ИИ ──");
            Console.ResetColor();

            WriteHelpLine("/test list", "список доступных ИИ");
            WriteHelpLine("/test <текст>", "тест всех ИИ твоим текстом");
            WriteHelpLine("/test quick", "быстрый тест заготовленной фразой");
            WriteHelpLine("/test <номер> <т>", "тест конкретного ИИ");
            WriteHelpLine("/test <номер> quick", "быстрый тест конкретного ИИ");

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  │  ── Двухуровневое редактирование ──");
            Console.ResetColor();

            WriteHelpLine("/guardian on|off", "вкл/выкл Guardian");
            WriteHelpLine("/guardian status", "статус Guardian + rollback");
            WriteHelpLine("/arc on|off", "авто-применение (Аркест)");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── Настройки ──");
            Console.ResetColor();

            WriteHelpLine("/think on|off", "показ хода мыслей");
            WriteHelpLine("/anim on|off", "анимации");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── Система ──");
            Console.ResetColor();

            WriteHelpLine("/status", "статус подключения");
            WriteHelpLine("/clear", "очистить экран");
            WriteHelpLine("/exit", "выход");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╰" + new string('─', winW - 4) + "╯");

            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void WriteHelpLine(string cmd, string desc)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  │    " + cmd.PadRight(20));

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(desc);

        Console.ResetColor();
    }

    // ══════════════════════════════════════════════════════════
    //  WRITE COLORED
    // ══════════════════════════════════════════════════════════
    static void WriteColored(ConsoleColor color, string text)
    {
        lock (PrintLock)
        {
            Console.ForegroundColor = color;
            Console.Write(text ?? "");
            Console.ResetColor();
        }
    }
}