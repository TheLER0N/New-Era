// Animations.cs — утилиты анимаций для консоли
// New Era CLI v4.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Threading;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  ANIMATIONS
    // ══════════════════════════════════════════════════════════

    static void TypewriterWrite(string text, ConsoleColor color, int delayMs)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (!AnimationsEnabled || delayMs <= 0)
        {
            lock (PrintLock)
            {
                Console.ForegroundColor = color;
                Console.Write(text);
                Console.ResetColor();
            }
            return;
        }

        lock (PrintLock)
        {
            Console.ForegroundColor = color;
            for (int i = 0; i < text.Length; i++)
            {
                if (StopRequested) break;
                Console.Write(text[i]);
                if (delayMs > 0 && i % 3 == 0) Thread.Sleep(delayMs);
            }
            Console.ResetColor();
        }
    }

    static void FadeInLines(string[] lines, ConsoleColor color, int delayMs)
    {
        if (lines == null) return;
        lock (PrintLock)
        {
            Console.ForegroundColor = color;
            for (int i = 0; i < lines.Length; i++)
            {
                if (StopRequested) break;
                Console.WriteLine(lines[i]);
                if (AnimationsEnabled && delayMs > 0) Thread.Sleep(delayMs);
            }
            Console.ResetColor();
        }
    }

    static void DrawProgressBar(int current, int total, string label)
    {
        lock (PrintLock)
        {
            int barWidth = 20;
            double ratio = total > 0 ? (double)current / total : 0;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            int filled = (int)(ratio * barWidth);
            if (filled > barWidth) filled = barWidth;

            Console.Write("\r  ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write((label ?? "") + " ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[");
            Console.ForegroundColor = ConsoleColor.Green;
            for (int i = 0; i < filled; i++) Console.Write("\u2588");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            for (int i = filled; i < barWidth; i++) Console.Write("\u2591");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("]");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" " + ((int)(ratio * 100)).ToString().PadLeft(3) + "%");
            Console.ResetColor();
            Console.Write("   ");
        }
    }

    static void ClearProgressBar(string label)
    {
        lock (PrintLock)
        {
            int len = (label ?? "").Length + 35;
            Console.Write("\r" + new string(' ', len) + "\r");
        }
    }

    static string AnimatedDots(int tick)
    {
        int count = (tick % 4);
        return new string('.', count);
    }

    static ConsoleColor SpinnerColor(int tick)
    {
        int phase = (tick / 3) % 4;
        switch (phase)
        {
            case 0: return ConsoleColor.Cyan;
            case 1: return ConsoleColor.Green;
            case 2: return ConsoleColor.Yellow;
            default: return ConsoleColor.Magenta;
        }
    }

    static void ShowNotification(string text, ConsoleColor color)
    {
        lock (PrintLock)
        {
            Console.ForegroundColor = color;
            Console.Write("\r  \u25B8 " + (text ?? "") + "   ");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    static void AnimDelay(int ms)
    {
        if (AnimationsEnabled && ms > 0 && !StopRequested)
            Thread.Sleep(ms);
    }
}