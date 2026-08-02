// Ui.cs — баннер, промпт, help, WriteColored
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

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

            try { winW = Console.WindowWidth; }
            catch { winW = 80; }

            if (winW < 40)
                winW = 40;

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭" + new string('─', winW - 4) + "╮");

            string left = "  │  ";
            string brand = "███  NEW ERA";
            string ver = "  v" + AppVersion;
            string sep = "  ·  Qwen CLI  ·  ";

            bool online = !string.IsNullOrEmpty(ChatId) && !string.IsNullOrEmpty(Token);
            string onlineText = online ? "● online" : "● offline";

            string indicators = "";

            if (DispatcherEnabled)
                indicators += "  ◆ dispatcher";

            if (CompressEnabled)
                indicators += "  ◆ compress";

            if (ExtractEnabled)
                indicators += "  ◆ extract";

            if (ArcMode)
                indicators += "  ◆ arc";

            int used =
                DisplayWidth(left) +
                DisplayWidth(brand) +
                DisplayWidth(ver) +
                DisplayWidth(sep) +
                DisplayWidth(onlineText) +
                DisplayWidth(indicators);

            int pad = winW - used - 1;

            if (pad < 1)
                pad = 1;

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(left);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(brand);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(ver);

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(sep);

            Console.ForegroundColor = online ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.Write(onlineText);

            if (indicators.Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write(indicators);
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(new string(' ', pad));
            Console.WriteLine("│");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ├" + new string('─', winW - 4) + "┤");
            Console.ResetColor();

            if (!online)
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

            Console.ForegroundColor = string.IsNullOrEmpty(Token)
                ? ConsoleColor.Yellow
                : ConsoleColor.Green;

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

            try { winW = Console.WindowWidth; }
            catch { winW = 80; }

            if (winW < 40)
                winW = 40;

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
            WriteHelpLine("/edit <папка> <з>", "создать/изменить файлы");
            WriteHelpLine("/plan <путь> <з>", "план реализации");
            WriteHelpLine("/scan <папка>", "отчёт по структуре");

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  │  ── Dispatcher v6.0 ──");
            Console.ResetColor();

            WriteHelpLine("/dispatcher on|off", "вкл/выкл v6-диспетчер");
            WriteHelpLine("/dispatcher status", "статус dispatcher");
            WriteHelpLine("/compress on|off", "сжатие контекста");
            WriteHelpLine("/extract on|off", "извлечение кода AI #2");
            WriteHelpLine("/arc on|off", "авто-применение (Аркест)");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  │  ── История ──");
            Console.ResetColor();

            WriteHelpLine("/history", "локальная история");
            WriteHelpLine("/history clear", "очистить историю");
            WriteHelpLine("/fetch", "история с сервера");
            WriteHelpLine("/live  /tail", "слежение за чатом");
            WriteHelpLine("/stop", "остановить live");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  │  ── Тест ИИ ──");
            Console.ResetColor();

            WriteHelpLine("/test list", "список доступных ИИ");
            WriteHelpLine("/test <текст>", "тест всех ИИ твоим текстом");
            WriteHelpLine("/test quick", "быстрый тест заготовленной фразой");
            WriteHelpLine("/test <номер> <т>", "тест конкретного ИИ");
            WriteHelpLine("/test <номер> quick", "быстрый тест конкретного ИИ");

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