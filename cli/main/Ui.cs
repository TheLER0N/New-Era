// Ui.cs — баннер, help, статус, рендер сообщений, спиннер, diff
// New Era v7.1
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

partial class MainConsole
{
    static readonly string[] SpinnerFrames = { "\u280B", "\u2819", "\u2839", "\u2838", "\u283C", "\u2834", "\u2826", "\u2827", "\u2807", "\u280F" };

    // ══════════════════════════════════════════════
    //  BANNER
    // ══════════════════════════════════════════════
    static void DrawBanner()
    {
        lock (PrintLock) {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 40) winW = 40;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u256D" + new string('\u2500', winW - 4) + "\u256E");

            bool online = !string.IsNullOrEmpty(ChatId) && !string.IsNullOrEmpty(Token);

            Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u2502  ");
            Console.ForegroundColor = ConsoleColor.Cyan;    Console.Write("\u2588\u2588\u2588  NEW ERA");
            Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  v" + AppVersion);
            Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u00B7  Qwen CLI  \u00B7  ");
            Console.ForegroundColor = online ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.Write(online ? "\u25CF online" : "\u25CF offline");

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("  \u25C6 v7 pipeline");

            int pad = winW - 55;
            if (pad < 1) pad = 1;

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(new string(' ', pad));
            Console.WriteLine("\u2502");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u251C" + new string('\u2500', winW - 4) + "\u2524");
            Console.ResetColor();

            if (!online) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  \u2502  \u26A0 Заполни qwen_config.txt (CHAT_ID, TOKEN)");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("  \u251C" + new string('\u2500', winW - 4) + "\u2524");
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  \u2502  \u25B8 /help — команды   \u25B8 /exit — выход   \u25B8 /status — статус");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void DrawPrompt()
    {
        lock (PrintLock) {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  ");
            Console.ForegroundColor = string.IsNullOrEmpty(Token) ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.Write("\u276F");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ");
            Console.ResetColor();
        }
    }

    // ══════════════════════════════════════════════
    //  HELP
    // ══════════════════════════════════════════════
    static void DrawHelp()
    {
        lock (PrintLock) {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 40) winW = 40;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u256D\u2500 \u25B8 КОМАНДЫ " + new string('\u2500', Math.Max(1, winW - 16)) + "\u256E");
            Console.ResetColor();

            WriteHelpLine("<текст>", "сообщение \u2192 ответ ИИ");
            WriteHelpLine("/edit <файл> <з>", "ИИ правит файл");
            WriteHelpLine("/edit <папка> <з>", "создать/изменить файлы");
            WriteHelpLine("/plan <путь> <з>", "план реализации");
            WriteHelpLine("/scan <папка>", "отчёт по структуре");
            WriteHelpLine("/test list", "список ИИ");
            WriteHelpLine("/test quick", "быстрый тест");
            WriteHelpLine("/history", "история");
            WriteHelpLine("/history clear", "очистить историю");
            WriteHelpLine("/fetch", "история с сервера");
            WriteHelpLine("/live  /tail", "слежение за чатом");
            WriteHelpLine("/stop", "остановить live");
            WriteHelpLine("/think on|off", "ход мыслей");
            WriteHelpLine("/status", "статус");
            WriteHelpLine("/clear", "очистить экран");
            WriteHelpLine("/exit", "выход");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void WriteHelpLine(string cmd, string desc)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("  \u2502    " + cmd.PadRight(22));
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(desc);
        Console.ResetColor();
    }

    // ══════════════════════════════════════════════
    //  STATUS
    // ══════════════════════════════════════════════
    static void DrawStatus()
    {
        // P0: фиксируем счётчик истории ДО входа в PrintLock
        int histCount;
        lock (HistoryLock) { histCount = History.Count; }

        lock (PrintLock) {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 40) winW = 40;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u256D\u2500 \u25B8 СТАТУС " + new string('\u2500', Math.Max(1, winW - 14)) + "\u256E");
            Console.ResetColor();

            WriteStatusLine("Chat ID", string.IsNullOrEmpty(ChatId) ? "\u2014" : ChatId, !string.IsNullOrEmpty(ChatId));
            WriteStatusLine("Token", string.IsNullOrEmpty(Token) ? "нет" : Token.Substring(0, Math.Min(8, Token.Length)) + "...", !string.IsNullOrEmpty(Token));
            WriteStatusLine("API", ApiBaseUrl, true);
            WriteStatusLine("Primary", PrimaryModel, true);
            WriteStatusLine("AI2 Model", GetAi2Model(), true);
            WriteStatusLine("AI2 Token", string.IsNullOrEmpty(Token2) ? "НЕТ" : Token2.Substring(0, Math.Min(8, Token2.Length)) + "...", !string.IsNullOrEmpty(Token2));
            WriteStatusLine("AI2 Chat", string.IsNullOrEmpty(ChatId2) ? "НЕТ" : ChatId2, !string.IsNullOrEmpty(ChatId2));
            WriteStatusLine("ArcMode", ArcMode ? "ON" : "OFF", ArcMode);
            WriteStatusLine("Версия", "v" + AppVersion, true);
            WriteStatusLine("История", histCount + "/" + MaxHistoryEntries, true);

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void DrawDispatcherStatus()
    {
        lock (PrintLock) {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  \u256D\u2500 \u25C6 PIPELINE v7 " + new string('\u2500', Math.Max(1, winW - 22)) + "\u256E");
            Console.ResetColor();

            WriteStatusLine("Dispatcher", DispatcherEnabled ? "ON" : "OFF", DispatcherEnabled);
            WriteStatusLine("Compress", CompressEnabled ? "ON" : "OFF", CompressEnabled);
            WriteStatusLine("Extract", ExtractEnabled ? "ON" : "OFF", ExtractEnabled);
            WriteStatusLine("Validate", Ai2ValidateEnabled ? "ON" : "OFF", Ai2ValidateEnabled);
            WriteStatusLine("ArcMode", ArcMode ? "ON" : "OFF", ArcMode);

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void WriteStatusLine(string label, string value, bool ok)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  \u2502  " + (label ?? "").PadRight(12));
        Console.ForegroundColor = ok ? ConsoleColor.Gray : ConsoleColor.Red;
        Console.WriteLine(value ?? "");
        Console.ResetColor();
    }

    // ══════════════════════════════════════════════
    //  RENDER MESSAGE
    // ══════════════════════════════════════════════
    static void RenderAssistantMessage(string text)
    {
        lock (PrintLock) {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 30) winW = 30;

            string time = DateTime.Now.ToString("HH:mm");
            int innerW = winW - 5;
            if (innerW < 20) innerW = 20;

            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u256D\u2500 ");
            Console.ForegroundColor = ConsoleColor.Magenta; Console.Write("\u25C6 Qwen");
            Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  " + time + " ");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            int fill = winW - 16 - time.Length;
            if (fill < 3) fill = 3;
            for (int i = 0; i < fill; i++) Console.Write("\u2500");
            Console.WriteLine("\u256E");

            string[] logical = (text ?? "").Split(new[] { "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;

            foreach (string ln in logical) {
                string l = ln.TrimEnd('\r').Replace("\t", "    ");

                if (l.TrimStart().StartsWith("```")) {
                    inCodeBlock = !inCodeBlock;
                    RenderBoxLine(l, innerW, ConsoleColor.DarkGray);
                    continue;
                }

                string[] wrapped = WrapText(l, innerW);
                foreach (string w in wrapped)
                    RenderBoxLine(w, innerW, inCodeBlock ? ConsoleColor.Gray : ConsoleColor.White);
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  \u2570");
            for (int i = 0; i < winW - 4; i++) Console.Write("\u2500");
            Console.WriteLine("\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void RenderBoxLine(string text, int innerWidth, ConsoleColor textColor)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write("  \u2502 ");
        Console.ForegroundColor = textColor;
        Console.Write(RenderPadRight(text ?? "", innerWidth));
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("\u2502");
    }

    static void RenderPlan(List<string> steps, string rawText, string projectPath)
    {
        lock (PrintLock) {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 44) winW = 44;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  \u256D\u2500 \u25C6 ПЛАН  \u00B7  " + steps.Count + " шагов ");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            int fill = winW - 30;
            if (fill < 3) fill = 3;
            for (int i = 0; i < fill; i++) Console.Write("\u2500");
            Console.WriteLine("\u256E");
            Console.ResetColor();

            RenderBoxLine("\u25B8 " + (projectPath ?? ""), winW - 5, ConsoleColor.DarkGray);

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  \u251C");
            for (int i = 0; i < winW - 4; i++) Console.Write("\u2500");
            Console.WriteLine("\u2524");

            for (int i = 0; i < steps.Count; i++) {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  \u2502 ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write((i + 1).ToString().PadLeft(2) + ". ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(steps[i]);
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  \u2570");
            for (int i = 0; i < winW - 4; i++) Console.Write("\u2500");
            Console.WriteLine("\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void ShowDiff(string[] original, int start, int end, string[] newLines)
    {
        lock (PrintLock) {
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u256D\u2500 \u25B8 DIFF " + new string('\u2500', 40) + "\u256E");
            Console.ResetColor();

            var oldSet = new HashSet<string>();
            for (int i = start; i <= end; i++) oldSet.Add(original[i].TrimEnd('\r'));

            var newSet = new HashSet<string>();
            foreach (string nl in newLines) newSet.Add(nl.TrimEnd('\r'));

            for (int i = start; i <= end; i++) {
                string ol = original[i].TrimEnd('\r');
                if (!newSet.Contains(ol)) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  \u2502 - " + ol);
                }
            }

            foreach (string nl in newLines) {
                string trimmed = nl.TrimEnd('\r');
                if (!oldSet.Contains(trimmed)) {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  \u2502 + " + trimmed);
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════
    //  SPINNER
    // ══════════════════════════════════════════════
    static void StartSpinner(string label)
    {
        StopSpinner();
        SpinnerActive = true;
        string safeLabel = label ?? "";

        SpinnerThread = new Thread(delegate() {
            int tick = 0;
            while (SpinnerActive && !StopRequested) {
                lock (PrintLock) {
                    string frame = SpinnerFrames[tick % SpinnerFrames.Length];
                    Console.ForegroundColor = SpinnerColor(tick);
                    Console.Write("\r  " + frame + " " + safeLabel + new string('.', tick % 4) + "   ");
                    Console.ResetColor();
                }
                tick++;
                Thread.Sleep(100);
            }

            lock (PrintLock) {
                int clearLen = safeLabel.Length + 12;
                Console.Write("\r" + new string(' ', clearLen) + "\r");
            }
        });

        SpinnerThread.IsBackground = true;
        SpinnerThread.Start();
    }

    static void StopSpinner()
    {
        SpinnerActive = false;
        if (SpinnerThread != null) {
            try { SpinnerThread.Join(500); } catch { }
            SpinnerThread = null;
        }
    }

    static ConsoleColor SpinnerColor(int tick)
    {
        switch ((tick / 3) % 4) {
            case 0: return ConsoleColor.Cyan;
            case 1: return ConsoleColor.Green;
            case 2: return ConsoleColor.Yellow;
            default: return ConsoleColor.Magenta;
        }
    }

    // ══════════════════════════════════════════════
    //  TEXT WRAP / WIDTH
    // ══════════════════════════════════════════════
    static string RenderPadRight(string text, int width)
    {
        int w = DisplayWidth(text);
        if (w >= width) return text;
        return text + new string(' ', width - w);
    }

    static string[] WrapText(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return new[] { "" };
        if (width < 1) width = 1;
        if (DisplayWidth(text) <= width) return new[] { text };

        var result = new List<string>();
        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        int currentW = 0;

        foreach (string word in words) {
            int wordW = DisplayWidth(word);

            if (wordW > width) {
                if (current.Length > 0) { result.Add(current.ToString()); current.Length = 0; currentW = 0; }

                var hw = new StringBuilder();
                int hwW = 0;
                for (int i = 0; i < word.Length; i++) {
                    int cw = IsWide(word[i]) ? 2 : 1;
                    if (hwW + cw > width) { result.Add(hw.ToString()); hw.Length = 0; hwW = 0; }
                    hw.Append(word[i]); hwW += cw;
                }
                if (hw.Length > 0) { current.Append(hw.ToString()); currentW = hwW; }
                continue;
            }

            if (current.Length == 0) { current.Append(word); currentW = wordW; }
            else if (currentW + 1 + wordW <= width) { current.Append(" "); current.Append(word); currentW += 1 + wordW; }
            else { result.Add(current.ToString()); current.Length = 0; current.Append(word); currentW = wordW; }
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result.ToArray();
    }

    static int DisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int w = 0;
        for (int i = 0; i < s.Length; i++) {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { w += 2; i++; }
            else if (IsWide(c)) w += 2;
            else w += 1;
        }
        return w;
    }

    static bool IsWide(char c)
    {
        return (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0x9FFF) ||
               (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0xF900 && c <= 0xFAFF) ||
               (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF01 && c <= 0xFF60) ||
               (c >= 0xFFE0 && c <= 0xFFE6);
    }

    static void WriteColored(ConsoleColor color, string text)
    {
        lock (PrintLock) {
            Console.ForegroundColor = color;
            Console.Write(text ?? "");
            Console.ResetColor();
        }
    }
}