// PlanMode.cs — plan-ядро: HandlePlan, RunSavedPlan, ParsePathAndTask, лимиты контекста
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.IO;

partial class MainConsole
{
    const int MaxContextTotal = 120000;
    const int MaxContextFile  = 40000;

    const int PlanMaxRetries   = 10;
    const int PlanRetryDelayMs = 3000;

    static string[] ParsePathAndTask(string args)
    {
        string trimmed = (args ?? "").Trim();

        string path;
        string task;

        if (trimmed.StartsWith("\""))
        {
            int close = trimmed.IndexOf('"', 1);

            if (close > 0)
            {
                path = trimmed.Substring(1, close - 1);
                task = trimmed.Substring(close + 1).Trim();
            }
            else
            {
                path = trimmed.Substring(1).TrimEnd('"');
                task = "";
            }
        }
        else
        {
            string[] words = trimmed.Split(' ');

            path = words[0];
            task = words.Length > 1
                ? string.Join(" ", words, 1, words.Length - 1)
                : "";

            if (!File.Exists(path) && !Directory.Exists(path) && words.Length > 2)
            {
                for (int i = 2; i <= words.Length; i++)
                {
                    string candidate = string.Join(" ", words, 0, i);

                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        path = candidate;
                        task = i < words.Length
                            ? string.Join(" ", words, i, words.Length - i)
                            : "";

                        break;
                    }
                }
            }
        }

        return new[] { path, task };
    }

    static void HandlePlan(string input)
    {
        string NL = Environment.NewLine;

        string args = input.Length > 5 ? input.Substring(5).Trim() : "";

        if (string.IsNullOrEmpty(args))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Использование: plan <путь> <задача>" + NL);

            WriteColored(ConsoleColor.DarkGray,
                "               plan \"C:\\My Folder\" <задача>" + NL);

            WriteColored(ConsoleColor.DarkGray,
                "               plan run [plan.txt] — повтор сохранённого плана" + NL);

            return;
        }

        if (args == "run" || args.StartsWith("run "))
        {
            RunSavedPlan(args.Length > 4 ? args.Substring(4).Trim() : "");
            return;
        }

        string[] parsed = ParsePathAndTask(args);

        string path = parsed[0];
        string task = parsed[1];

        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.DarkGray,
                "  ◌ Введи задачу (пустая строка = конец):" + NL);

            task = ReadMultiline();
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустая задача. Отмена." + NL);

            return;
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Недопустимый путь: " + ex.Message + NL);

            return;
        }

        string structure = "";

        if (Directory.Exists(fullPath))
        {
            structure = ScanDirectory(fullPath, 0);
        }
        else if (File.Exists(fullPath))
        {
            structure = "FILE: " + fullPath;
        }
        else
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Путь не найден: " + fullPath + NL);

            return;
        }

        if (DispatcherEnabled)
        {
            HandlePlanV6(fullPath, task, structure);
            return;
        }

        string prompt =
            "Составь план реализации задачи." + NL +
            "Задача: " + task + NL +
            "Структура проекта:" + NL + structure + NL +
            "Верни нумерованный план действий. " +
            "Формат: N. [ДЕЙСТВИЕ] Файл — описание" + NL +
            "Правила:" + NL +
            "- Один шаг = один файл (правки одного файла группируй в один шаг)." + NL +
            "- Только нужные шаги, без воды." + NL +
            "- Без вступлений и пояснений вне списка.";

        string codePayload = BuildContextPayload(fullPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(codePayload))
        {
            prompt +=
                NL + "Current source files (use as ground truth):" + NL + codePayload +
                NL + "If required files are missing, start with NEED FILES: paths." + NL;
        }

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Отправка в ИИ (планирование)..." + NL);

        AddHistory("user", "[plan] " + path + " " + task);

        StartSpinner("план");

        string responseText = null;

        try
        {
            string raw = PostMessage(prompt, LastResponseId);
            responseText = ParseSseAnswer(raw);

            if (string.IsNullOrWhiteSpace(responseText))
                responseText = ParseOrchestratorResponse(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + NL);

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ." + NL);

            return;
        }

        AddHistory("assistant", responseText);

        List<string> steps = ParsePlanSteps(responseText);

        RenderPlan(steps, responseText, fullPath);

        if (steps.Count > 0)
            PlanActionMenu(steps, fullPath, task, structure);
    }

    static void RunSavedPlan(string planFileArg)
    {
        string NL = Environment.NewLine;

        string planPath;

        try
        {
            planPath = Path.GetFullPath(
                string.IsNullOrEmpty(planFileArg) ? "plan.txt" : planFileArg);
        }
        catch
        {
            planPath = Path.Combine(BaseDir, "plan.txt");
        }

        if (!File.Exists(planPath))
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ План не найден: " + planPath + NL);

            WriteColored(ConsoleColor.DarkGray,
                "  ◌ Сначала: /plan <путь> <задача> → [3] сохранить" + NL);

            return;
        }

        string content = ReadTextAuto(planPath);

        string projectPath = BaseDir;
        string task = "";

        foreach (string raw in content.Split(new[] { (char)10 }, StringSplitOptions.None))
        {
            string l = raw.TrimEnd((char)13);

            if (l.StartsWith("PROJECT: "))
                projectPath = l.Substring(9).Trim();
            else if (l.StartsWith("TASK: "))
                task = l.Substring(6).Trim();
        }

        List<string> steps = ParsePlanSteps(content);

        if (steps.Count == 0)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ В файле нет шагов плана." + NL);

            return;
        }

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ План из файла: " + planPath +
            " (без запроса на планирование)" + NL);

        string structure = "";

        if (Directory.Exists(projectPath))
            structure = ScanDirectory(projectPath, 0);

        RenderPlan(steps, null, projectPath);

        PlanActionMenu(steps, projectPath, task, structure);
    }
}