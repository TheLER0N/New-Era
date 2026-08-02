// Spinner.cs — анимированный спиннер ожидания
// New Era CLI v4.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Threading;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  SPINNER
    // ══════════════════════════════════════════════════════════

    static readonly string[] SpinnerFrames = { "\u280B", "\u2819", "\u2839", "\u2838", "\u283C", "\u2834", "\u2826", "\u2827", "\u2807", "\u280F" };

    static void StartSpinner(string label)
    {
        StopSpinner();
        SpinnerActive = true;

        string safeLabel = label ?? "";

        SpinnerThread = new Thread(() =>
        {
            int tick = 0;
            while (SpinnerActive && !StopRequested)
            {
                lock (PrintLock)
                {
                    string frame = SpinnerFrames[tick % SpinnerFrames.Length];
                    ConsoleColor color = SpinnerColor(tick);
                    Console.ForegroundColor = color;
                    Console.Write("\r  " + frame + " " + safeLabel + AnimatedDots(tick) + "   ");
                    Console.ResetColor();
                }
                tick++;
                Thread.Sleep(100);
            }

            lock (PrintLock)
            {
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
        if (SpinnerThread != null)
        {
            try { SpinnerThread.Join(500); } catch { }
            SpinnerThread = null;
        }
    }
}