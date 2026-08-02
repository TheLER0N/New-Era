// Render.cs — рендер сообщений, перенос по ширине, подсветка кода
// New Era CLI v4.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.Text;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  RENDER
    // ══════════════════════════════════════════════════════════

    static void RenderAssistantMessage(string text)
    {
        lock (PrintLock)
        {
            int winW;
            try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 30) winW = 30;

            string time = DateTime.Now.ToString("HH:mm");

            int innerW = winW - 5;
            if (innerW < 20) innerW = 20;

            Console.WriteLine();

            string topPrefix = "  \u256D\u2500 ";
            string topTitle = "\u25C6 Qwen";
            string topTime = "  " + time + " ";

            int headerW = DisplayWidth(topPrefix) + DisplayWidth(topTitle) + DisplayWidth(topTime);
            int topFill = winW - headerW - 1;
            if (topFill < 3) topFill = 3;

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(topPrefix);
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write(topTitle);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(topTime);
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            for (int i = 0; i < topFill; i++) Console.Write("\u2500");
            Console.WriteLine("\u256E");

            string[] logical = (text ?? "").Split(new[] { "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;

            foreach (string ln in logical)
            {
                string l = ln.TrimEnd('\r').Replace("\t", "    ");

                if (l.TrimStart().StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;
                    string[] lines = RenderHardWrapLine(l, innerW);
                    for (int i = 0; i < lines.Length; i++)
                        RenderBoxLine(lines[i], innerW, ConsoleColor.DarkGray);
                    Console.ResetColor();
                    continue;
                }

                if (inCodeBlock)
                {
                    string[] lines = RenderHardWrapLine(l, innerW);
                    for (int i = 0; i < lines.Length; i++)
                        RenderBoxLine(lines[i], innerW, ConsoleColor.Gray);
                    Console.ResetColor();
                    continue;
                }

                string[] wrapped = WrapText(l, innerW);
                for (int w = 0; w < wrapped.Length; w++)
                    RenderBoxLine(wrapped[w], innerW, ConsoleColor.White);
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            string bottomPrefix = "  \u2570";
            string bottomSuffix = "\u256F";
            int bottomFill = winW - DisplayWidth(bottomPrefix) - DisplayWidth(bottomSuffix);
            if (bottomFill < 3) bottomFill = 3;

            Console.Write(bottomPrefix);
            for (int i = 0; i < bottomFill; i++) Console.Write("\u2500");
            Console.WriteLine(bottomSuffix);
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

    static string RenderPadRight(string text, int width)
    {
        int w = DisplayWidth(text);
        if (w >= width) return text;
        return text + new string(' ', width - w);
    }

    static string[] RenderHardWrapLine(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return new[] { "" };
        if (width < 1) width = 1;
        if (DisplayWidth(text) <= width) return new[] { text };

        var result = new List<string>();
        var current = new StringBuilder();
        int currentW = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int charW;
            bool pair = false;

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                charW = 2;
                pair = true;
            }
            else
            {
                charW = IsWide(c) ? 2 : 1;
            }

            if (currentW + charW > width)
            {
                result.Add(current.ToString());
                current.Length = 0;
                currentW = 0;
            }

            current.Append(c);
            if (pair)
            {
                current.Append(text[i + 1]);
                i++;
            }
            currentW += charW;
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result.ToArray();
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

        foreach (string word in words)
        {
            int wordW = DisplayWidth(word);

            if (wordW > width)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Length = 0;
                    currentW = 0;
                }

                foreach (string part in RenderHardWrapWord(word, width))
                    result.Add(part);

                continue;
            }

            if (current.Length == 0)
            {
                current.Append(word);
                currentW = wordW;
            }
            else
            {
                if (currentW + 1 + wordW <= width)
                {
                    current.Append(" ");
                    current.Append(word);
                    currentW += 1 + wordW;
                }
                else
                {
                    result.Add(current.ToString());
                    current.Length = 0;
                    current.Append(word);
                    currentW = wordW;
                }
            }
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result.ToArray();
    }

    static List<string> RenderHardWrapWord(string word, int width)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(word)) return result;
        if (width < 1) width = 1;

        var current = new StringBuilder();
        int currentW = 0;

        for (int i = 0; i < word.Length; i++)
        {
            char c = word[i];
            int charW;
            bool pair = false;

            if (char.IsHighSurrogate(c) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]))
            {
                charW = 2;
                pair = true;
            }
            else
            {
                charW = IsWide(c) ? 2 : 1;
            }

            if (currentW + charW > width)
            {
                result.Add(current.ToString());
                current.Length = 0;
                currentW = 0;
            }

            current.Append(c);
            if (pair)
            {
                current.Append(word[i + 1]);
                i++;
            }
            currentW += charW;
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    static int DisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int w = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                w += 2;
                i++;
            }
            else if (IsWide(c))
                w += 2;
            else
                w += 1;
        }
        return w;
    }

    static bool IsWide(char c)
    {
        return (c >= 0x1100 && c <= 0x115F) ||
               (c >= 0x2E80 && c <= 0x9FFF) ||
               (c >= 0xAC00 && c <= 0xD7AF) ||
               (c >= 0xF900 && c <= 0xFAFF) ||
               (c >= 0xFE30 && c <= 0xFE6F) ||
               (c >= 0xFF01 && c <= 0xFF60) ||
               (c >= 0xFFE0 && c <= 0xFFE6);
    }
}