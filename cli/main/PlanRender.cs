// PlanRender.cs — рендер плана: парсинг шагов, отрисовка рамки и шагов, цвета действий
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

partial class MainConsole
{
    static List<string> ParsePlanSteps(string text)
    {
        var steps = new List<string>();
        string[] lines = (text ?? "").Split(new[] { "\n" }, StringSplitOptions.None);
        foreach (string raw in lines)
        {
            string l = raw.TrimEnd('\r').Trim();
            if (Regex.IsMatch(l, @"^\d+[\.\)]\s"))
                steps.Add(Regex.Replace(l, @"^\d+[\.\)]\s*", ""));
            else if (l.StartsWith("- ") && l.Contains("["))
                steps.Add(l.Substring(2));
        }
        return steps;
    }

    static void RenderPlan(List<string> steps, string rawText, string projectPath)
    {
        lock (PrintLock)
        {
            int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
            if (winW < 44) winW = 44;
            int innerW = winW - 5;
            string time = DateTime.Now.ToString("HH:mm");
            Console.WriteLine();
            PlanTopBorder("◆ ПЛАН  " + time + "  ·  " + steps.Count + " " + StepsWord(steps.Count), winW);
            RenderBoxLine("▸ " + (projectPath ?? ""), innerW, ConsoleColor.DarkGray);
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  ├"); for (int i = 0; i < winW - 4; i++) Console.Write("─"); Console.WriteLine("┤");
            if (steps.Count == 0) {
                string[] lines = (rawText ?? "").Split(new[] { "\n" }, StringSplitOptions.None);
                foreach (string ln in lines) RenderBoxLine(ln.TrimEnd('\r'), innerW, ConsoleColor.White);
            } else {
                RenderBoxLine("", innerW, ConsoleColor.DarkGray);
                for (int i = 0; i < steps.Count; i++) {
                    RenderPlanStep(i + 1, steps[i], innerW);
                    if (i < steps.Count - 1) RenderBoxLine("", innerW, ConsoleColor.DarkGray);
                }
                RenderBoxLine("", innerW, ConsoleColor.DarkGray);
            }
            PlanBottomBorder(winW);
            Console.WriteLine();
        }
    }

    static void RenderPlanStep(int num, string step, int innerW)
    {
        string numStr = num.ToString().PadLeft(2) + ".";
        string action, filePart, descPart;
        TryParsePlanStep(step, out action, out filePart, out descPart);
        int numW = DisplayWidth(numStr) + 1;
        int actionW = action != null ? DisplayWidth("[" + action + "]  ") : 0;
        int padBase = numW + actionW;

        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  │ ");
        Console.ForegroundColor = ConsoleColor.Gray; Console.Write(numStr + " ");
        if (action != null) { Console.ForegroundColor = ActionColor(action); Console.Write("[" + action + "]  "); }

        if (!string.IsNullOrWhiteSpace(filePart)) {
            int fileW = DisplayWidth(filePart); int sepW = 3; int descW = innerW - padBase - fileW - sepW;
            if (descW < 10) {
                Console.ForegroundColor = ConsoleColor.Cyan; Console.Write(filePart);
                PlanEndLine(padBase + fileW, innerW);
                int wide = innerW - padBase; if (wide < 10) wide = 10;
                string[] wrapped = WrapText(descPart ?? "", wide);
                for (int w = 0; w < wrapped.Length; w++) RenderBoxLine(new string(' ', padBase) + wrapped[w], innerW, ConsoleColor.White);
                Console.ResetColor(); return;
            }
            string[] wrappedFirst = WrapText(descPart ?? "", descW);
            Console.ForegroundColor = ConsoleColor.Cyan; Console.Write(filePart);
            Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write(" — ");
            Console.ForegroundColor = ConsoleColor.White; Console.Write(wrappedFirst[0]);
            int used = padBase + fileW + sepW + DisplayWidth(wrappedFirst[0]);
            PlanEndLine(used, innerW);
            for (int w = 1; w < wrappedFirst.Length; w++) RenderBoxLine(new string(' ', padBase) + wrappedFirst[w], innerW, ConsoleColor.White);
        } else {
            int restW = innerW - padBase; if (restW < 10) restW = 10;
            string text = !string.IsNullOrWhiteSpace(descPart) ? descPart : step;
            string[] wrapped = WrapText(text, restW);
            Console.ForegroundColor = ConsoleColor.White; Console.Write(wrapped[0]);
            int used = padBase + DisplayWidth(wrapped[0]); PlanEndLine(used, innerW);
            for (int w = 1; w < wrapped.Length; w++) RenderBoxLine(new string(' ', padBase) + wrapped[w], innerW, ConsoleColor.White);
        }
        Console.ResetColor();
    }

    static void PlanTopBorder(string headText, int winW) {
        string head = (headText ?? "") + " "; int headW = 5 + DisplayWidth(head); int fill = winW - headW - 1; if (fill < 3) fill = 3;
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  ╭─ ");
        Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(head);
        Console.ForegroundColor = ConsoleColor.DarkCyan; for (int i = 0; i < fill; i++) Console.Write("─"); Console.WriteLine("╮");
    }
    static void PlanBottomBorder(int winW) {
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  ╰");
        for (int i = 0; i < winW - 4; i++) Console.Write("─"); Console.WriteLine("╯"); Console.ResetColor();
    }
    static void PlanEndLine(int usedW, int innerW) {
        Console.ForegroundColor = ConsoleColor.DarkCyan; if (usedW < innerW) Console.Write(new string(' ', innerW - usedW)); Console.WriteLine("│");
    }
    static string StepsWord(int n) { int m = n % 100, d = n % 10; if (m >= 11 && m <= 14) return "шагов"; if (d == 1) return "шаг"; if (d >= 2 && d <= 4) return "шага"; return "шагов"; }
    static string RequestsWord(int n) { int m = n % 100, d = n % 10; if (m >= 11 && m <= 14) return "запросов"; if (d == 1) return "запрос"; if (d >= 2 && d <= 4) return "запроса"; return "запросов"; }
    static ConsoleColor ActionColor(string action) {
        string a = action.ToUpperInvariant();
        if (a.Contains("СОЗДАТЬ") || a.Contains("ДОБАВИТЬ") || a.Contains("CREATE") || a.Contains("ADD")) return ConsoleColor.Green;
        if (a.Contains("ИСПРАВИТЬ") || a.Contains("ОБНОВИТЬ") || a.Contains("ПОПРАВИТЬ") || a.Contains("FIX") || a.Contains("UPDATE")) return ConsoleColor.Yellow;
        if (a.Contains("УДАЛИТЬ") || a.Contains("DELETE") || a.Contains("REMOVE")) return ConsoleColor.Red;
        if (a.Contains("ПРОВЕРИТЬ") || a.Contains("ТЕСТ") || a.Contains("CHECK") || a.Contains("TEST")) return ConsoleColor.Cyan;
        if (a.Contains("АНАЛИЗ") || a.Contains("ИЗУЧИТЬ") || a.Contains("ПРОЧИТАТЬ") || a.Contains("ANALYZE") || a.Contains("READ")) return ConsoleColor.Magenta;
        return ConsoleColor.Gray;
    }
}