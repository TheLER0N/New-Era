// Ui.cs — баннер, помощь, статус, рендер ответов
// New Era v7.2
using System;
using System.Collections.Generic;
using System.Text;
partial class MainConsole
{
static void DrawBanner()
{
lock (PrintLock) {
int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
if (winW < 55) winW = 55;
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u256D" + new string('\u2500', winW - 4) + "\u256E");

        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u2502 ");
        Console.ForegroundColor = ConsoleColor.Magenta; Console.Write(" \u2588\u2588\u2588  ");
        Console.ForegroundColor = ConsoleColor.White; Console.Write(" NEW ERA  ");
        Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write(" v" + AppVersion);
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write(" \u00B7 Qwen CLI \u00B7 ");
        Console.ForegroundColor = ConsoleColor.Green; Console.Write("\u25CF online");
        Console.ForegroundColor = ConsoleColor.Magenta; Console.Write(" \u25C6 v7 pipeline");

        int pad = winW - 55;
        if (pad < 1) pad = 1;
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write(new string(' ', pad));
        Console.WriteLine("\u2502");

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u251C" + new string('\u2500', winW - 4) + "\u2524");
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

static void DrawHelp()
{
    lock (PrintLock) {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 44) winW = 44;

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u256D\u2500 \u25B8 КОМАНДЫ " + new string('\u2500', Math.Max(1, winW - 15)) + "\u256E");
        Console.ResetColor();

        WriteHelpLine("/help", "эта справка");
        WriteHelpLine("/exit", "выход");
        WriteHelpLine("/status", "статус конфигурации");
        WriteHelpLine("/history", "история чата");
        WriteHelpLine("/history clear", "очистить историю");
        WriteHelpLine("/say <текст>", "отправить сообщение");
        WriteHelpLine("/edit <путь> <з>", "редактировать файл/папку");
        WriteHelpLine("/plan <путь> <з>", "составить план");
        WriteHelpLine("/plan run [файл]", "запустить сохранённый план");
        WriteHelpLine("/scan <папка>", "структура папки");
        WriteHelpLine("/idea <путь> <з>", "брейншторм идей");
        WriteHelpLine("/test [N|list]", "тест ИИ");
        WriteHelpLine("/fetch", "загрузить историю (снапшот)");
        WriteHelpLine("/live", "live-мониторинг");
        WriteHelpLine("/tail", "live только новые");
        WriteHelpLine("/stop", "остановить live");
        WriteHelpLine("/think on|off", "ход мыслей");
        WriteHelpLine("/anim on|off", "анимации");
        WriteHelpLine("/dispatcher status", "статус AI#2");

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
        Console.ResetColor();
        Console.WriteLine();
    }
}

static void WriteHelpLine(string cmd, string desc)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("  \u2502 " + cmd.PadRight(22));
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine(desc);
    Console.ResetColor();
}

static void DrawStatus()
{
    lock (PrintLock) {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 44) winW = 44;

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u256D\u2500 \u25B8 СТАТУС " + new string('\u2500', Math.Max(1, winW - 12)) + "\u256E");
        Console.ResetColor();

        WriteStatusLine("Token", string.IsNullOrEmpty(Token) ? "НЕТ" : Token.Substring(0, Math.Min(8, Token.Length)) + "...", !string.IsNullOrEmpty(Token));
        WriteStatusLine("Chat ID", string.IsNullOrEmpty(ChatId) ? "НЕТ" : ChatId, !string.IsNullOrEmpty(ChatId));
        WriteStatusLine("API", ApiBaseUrl, true);
        WriteStatusLine("Primary", PrimaryModel, true);
        WriteStatusLine("AI2 Model", GetAi2Model(), true);
        WriteStatusLine("AI2 Token", string.IsNullOrEmpty(Token2) ? "НЕТ" : Token2.Substring(0, Math.Min(8, Token2.Length)) + "...", !string.IsNullOrEmpty(Token2));
        WriteStatusLine("AI2 Chat", string.IsNullOrEmpty(ChatId2) ? "НЕТ" : ChatId2, !string.IsNullOrEmpty(ChatId2));
        WriteStatusLine("ArcMode", ArcMode ? "ON" : "OFF", ArcMode);
        WriteStatusLine("Версия", "v" + AppVersion, true);

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
        Console.ResetColor();
        Console.WriteLine();
    }
}

static void WriteStatusLine(string label, string value, bool ok)
{
    Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u2502 ");
    Console.ForegroundColor = ConsoleColor.Gray; Console.Write(label.PadRight(14));
    Console.ForegroundColor = ok ? ConsoleColor.White : ConsoleColor.Red;
    Console.WriteLine(value);
    Console.ResetColor();
}

static void DrawDispatcherStatus()
{
    lock (PrintLock) {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 44) winW = 44;

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u256D\u2500 \u25B8 DISPATCHER " + new string('\u2500', Math.Max(1, winW - 16)) + "\u256E");
        Console.ResetColor();

        WriteStatusLine("Dispatcher", DispatcherEnabled ? "ON" : "OFF", DispatcherEnabled);
        WriteStatusLine("Compress", CompressEnabled ? "ON" : "OFF", CompressEnabled);
        WriteStatusLine("Extract", ExtractEnabled ? "ON" : "OFF", ExtractEnabled);
        WriteStatusLine("Validate", Ai2ValidateEnabled ? "ON" : "OFF", Ai2ValidateEnabled);
        WriteStatusLine("AI2 Config", IsAi2Configured() ? "OK" : "НЕТ", IsAi2Configured());

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
        Console.ResetColor();
        Console.WriteLine();
    }
}

static void RenderAssistantMessage(string text)
{
    if (string.IsNullOrEmpty(text)) return;
    lock (PrintLock) {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        Console.WriteLine(text);
        Console.ResetColor();
        Console.WriteLine();
    }
}

static void RenderPlan(List<string> steps, string rawText, string projectPath)
{
    lock (PrintLock) {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 44) winW = 44;

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  \u256D\u2500 \u25C6 ПЛАН \u00B7 " + steps.Count + " шагов ");
        Console.WriteLine(new string('\u2500', Math.Max(1, winW - 20 - steps.Count.ToString().Length)) + "\u256E");
        Console.ResetColor();

        if (!string.IsNullOrEmpty(projectPath)) {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  \u2502 \u25B8 " + projectPath);
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u251C" + new string('\u2500', winW - 4) + "\u2524");
        Console.ResetColor();

        for (int i = 0; i < steps.Count; i++) {
            string stepText = (i + 1) + ". " + steps[i];
            List<string> wrapped = WrapText(stepText, winW - 8);
            for (int j = 0; j < wrapped.Count; j++) {
                Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u2502 ");
                Console.ForegroundColor = j == 0 ? ConsoleColor.White : ConsoleColor.Gray;
                Console.WriteLine(wrapped[j]);
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
        Console.ResetColor();
        Console.WriteLine();
    }
}

static List<string> WrapText(string text, int maxWidth)
{
    var lines = new List<string>();
    if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
    if (maxWidth < 10) maxWidth = 10;

    int pos = 0;
    while (pos < text.Length) {
        int len = Math.Min(maxWidth, text.Length - pos);
        if (pos + len < text.Length) {
            int brk = text.LastIndexOf(' ', pos + len - 1, len);
            if (brk > pos) len = brk - pos;
        }
        lines.Add(text.Substring(pos, len));
        pos += len;
        if (pos < text.Length && text[pos] == ' ') pos++;
    }
    return lines;
}

static void WriteColored(ConsoleColor color, string text)
{
    lock (PrintLock) {
        Console.ForegroundColor = color;
        Console.Write(text ?? "");
        Console.ResetColor();
    }
}

static void StartSpinner(string label)
{
    if (!AnimationsEnabled || SpinnerActive) return;
    SpinnerActive = true;
    SpinnerThread = new System.Threading.Thread(delegate() {
        string[] frames = { "\u2839", "\u2838", "\u2834", "\u2826", "\u2807", "\u280F", "\u2819", "\u2839" };
        int i = 0;
        while (SpinnerActive && !StopRequested) {
            lock (PrintLock) {
                Console.Write("\r  " + frames[i % frames.Length] + " " + label + "...");
            }
            i++;
            System.Threading.Thread.Sleep(100);
        }
        lock (PrintLock) { Console.Write("\r" + new string(' ', label.Length + 10) + "\r"); }
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
    lock (PrintLock) { Console.Write("\r" + new string(' ', 60) + "\r"); }
}
}