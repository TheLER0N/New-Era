// PlanMenu.cs — меню действий плана и сохранение плана в файл
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    static void PlanActionMenu(List<string> steps, string projectPath, string originalTask, string structure)
    {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 44) winW = 44; int innerW = winW - 5;
        lock (PrintLock) {
            PlanTopBorder("◆ ДЕЙСТВИЯ", winW);
            PlanMenuLine("1", "Выполнить пошагово", "≈ " + steps.Count + " " + RequestsWord(steps.Count), ConsoleColor.Green, innerW);
            PlanMenuLine("2", "Пошагово · авто-режим", "без подтверждений", ConsoleColor.Green, innerW);
            PlanMenuLine("3", "Всё за 1 запрос", "★ экономит лимит", ConsoleColor.Cyan, innerW);
            PlanMenuLine("4", "Сохранить в plan.txt", "", ConsoleColor.Gray, innerW);
            PlanMenuLine("5", "Отмена", "", ConsoleColor.Gray, innerW);
            PlanBottomBorder(winW); Console.WriteLine();
        }
        Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  ❯ "); Console.ResetColor();
        string choice = Console.ReadLine(); if (choice == null) return; choice = choice.Trim();
        if (choice == "1") ExecutePlan(steps, projectPath, originalTask, false);
        else if (choice == "2") ExecutePlan(steps, projectPath, originalTask, true);
        else if (choice == "3") ExecutePlanOneRequest(steps, projectPath, originalTask, structure);
        else if (choice == "4") SavePlanToFile(steps, projectPath, originalTask);
        else WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n");
    }

    static void PlanMenuLine(string num, string label, string hint, ConsoleColor color, int innerW) {
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  │ ");
        Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  [" + num + "] ");
        Console.ForegroundColor = color; Console.Write(label);
        int used = 2 + 1 + num.Length + 2 + DisplayWidth(label);
        while (used < 28) { Console.Write(" "); used++; }
        Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write(hint); used += DisplayWidth(hint);
        PlanEndLine(used, innerW);
    }

    static void SavePlanToFile(List<string> steps, string projectPath, string task) {
        try {
            string dir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);
            if (string.IsNullOrEmpty(dir)) dir = BaseDir;
            string planFile = Path.Combine(dir, "plan.txt");
            var sb = new StringBuilder();
            sb.AppendLine("PLAN · " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            sb.AppendLine("PROJECT: " + projectPath);
            sb.AppendLine("TASK: " + (task ?? ""));
            sb.AppendLine(new string('=', 50));
            for (int i = 0; i < steps.Count; i++) sb.AppendLine((i + 1) + ". " + steps[i]);
            File.WriteAllText(planFile, sb.ToString(), new UTF8Encoding(false));
            WriteColored(ConsoleColor.Green, " ✔ Сохранён: " + planFile + "\n");
            WriteColored(ConsoleColor.DarkGray, "    Повтор: /plan run\n");
        } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  ✖ " + ex.Message + "\n"); }
    }
}