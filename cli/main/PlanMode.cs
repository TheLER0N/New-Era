// PlanMode.cs — plan-ядро: HandlePlan, RunSavedPlan, ParsePathAndTask, лимиты контекста
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// Рефакторинг v5.2: файл раздроблен по зонам ответственности (100–300 строк на файл).
//   PlanMode.cs       — plan-ядро (этот файл)
//   GuardianLog.cs    — RollbackEntry, rollback/logging, ValidateFileContentWithGuardian
//   Scan.cs           — HandleScan, ScanDirectory
//   PlanRender.cs     — ParsePlanSteps, RenderPlan, RenderPlanStep, рамки, цвета
//   PlanMenu.cs       — PlanActionMenu, PlanMenuLine, SavePlanToFile
//   PlanExecute.cs    — ExecutePlan, ExecuteReadStep, SayStepWithContext
//   PlanOneRequest.cs — ExecutePlanOneRequest
//   PlanHelpers.cs    — TryParsePlanStep, ResolvePlanFile, BuildPlanFilePayload, ParsePlanFileBlocks
//   ContextBuilder.cs — BuildContextPayload, CollectContextFiles, IsExcluded*
using System;
using System.Collections.Generic;
using System.IO;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  CONTEXT LIMITS — разумные дефолты (fallback без оркестратора)
    // ══════════════════════════════════════════════════════════
    const int MaxContextTotal = 120000;
    const int MaxContextFile  = 40000;

    // v5.2: 5 попыток на шаг плана
    const int PlanMaxRetries   = 5;
    const int PlanRetryDelayMs = 3000;

    // ══════════════════════════════════════════════════════════
    //  PARSE PATH + TASK (общий для plan / edit)
    // ══════════════════════════════════════════════════════════
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

    // ══════════════════════════════════════════════════════════
    //  PLAN MODE
    // ══════════════════════════════════════════════════════════
    static void HandlePlan(string input)
    {
        string args = input.Length > 5 ? input.Substring(5).Trim() : "";
        if (string.IsNullOrEmpty(args))
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ Использование: plan <путь> <задача>\n");
            WriteColored(ConsoleColor.DarkGray, "               plan \"C:\\My Folder\" <задача>\n");
            WriteColored(ConsoleColor.DarkGray, "               plan run [plan.txt] — повтор сохранённого плана\n");
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
            WriteColored(ConsoleColor.DarkGray, "  ◌ Введи задачу (пустая строка = конец):\n");
            task = ReadMultiline();
        }
        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ Пустая задача. Отмена.\n");
            return;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  ✖ Недопустимый путь: " + ex.Message + "\n");
            return;
        }

        string structure = "";
        if (Directory.Exists(fullPath))
            structure = ScanDirectory(fullPath, 0);
        else if (File.Exists(fullPath))
            structure = "FILE: " + fullPath;
        else
        {
            WriteColored(ConsoleColor.Red, "  ✖ Путь не найден: " + fullPath + "\n");
            return;
        }

        string prompt =
            "Составь план реализации задачи.\n" +
            "Задача: " + task + "\n" +
            "Структура проекта:\n" + structure + "\n" +
            "Верни нумерованный план действий. " +
            "Формат: N. [ДЕЙСТВИЕ] Файл — описание\n" +
            "Правила:\n" +
            "- Один шаг = один файл (правки одного файла группируй в один шаг).\n" +
            "- Только нужные шаги, без воды.\n" +
            "- Без вступлений и пояснений вне списка.";

        // v5.2 FIX: селективный контекст ТОЛЬКО при OrchestratorEnabled.
        string codePayload = null;
        if (OrchestratorEnabled)
        {
            codePayload = BuildSelectivePayload(AnalyzeAndSelectFiles(task, fullPath), fullPath);
            if (string.IsNullOrEmpty(codePayload))
            {
                WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: контекст пуст — fallback на локальный скан\n");
                codePayload = BuildContextPayload(fullPath, MaxContextTotal, MaxContextFile);
            }
            else
            {
                WriteColored(ConsoleColor.DarkGray, "  ◌ orchestrator: контекст подобран\n");
            }
        }
        else
        {
            codePayload = BuildContextPayload(fullPath, MaxContextTotal, MaxContextFile);
        }

        if (!string.IsNullOrEmpty(codePayload))
        {
            prompt += "\nCurrent source files (use as ground truth):\n" + codePayload +
                      "\nIf required files are missing, start with NEED FILES: paths.\n";
        }

        WriteColored(ConsoleColor.DarkGray, "  ◌ Отправка в ИИ (планирование)...\n");
        AddHistory("user", "[plan] " + path + " " + task);

        StartSpinner("план");
        string responseText = null;
        try
        {
            string raw = PostMessage(prompt, LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();
            WriteColored(ConsoleColor.Red, "  ✖ Ошибка: " + ex.Message + "\n");
            return;
        }
        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ Пустой ответ.\n");
            return;
        }

        AddHistory("assistant", responseText);
        List<string> steps = ParsePlanSteps(responseText);
        RenderPlan(steps, responseText, fullPath);

        if (steps.Count > 0)
            PlanActionMenu(steps, fullPath, task, structure);
    }

    // ══════════════════════════════════════════════════════════
    //  PLAN RUN
    // ══════════════════════════════════════════════════════════
    static void RunSavedPlan(string planFileArg)
    {
        string planPath;
        try
        {
            planPath = Path.GetFullPath(string.IsNullOrEmpty(planFileArg) ? "plan.txt" : planFileArg);
        }
        catch
        {
            planPath = Path.Combine(BaseDir, "plan.txt");
        }

        if (!File.Exists(planPath))
        {
            WriteColored(ConsoleColor.Red, "  ✖ План не найден: " + planPath + "\n");
            WriteColored(ConsoleColor.DarkGray, "  ◌ Сначала: /plan <путь> <задача> → [3] сохранить\n");
            return;
        }

        string content = ReadTextAuto(planPath);
        string projectPath = BaseDir;
        string task = "";

        foreach (string raw in content.Split(new[] { "\n" }, StringSplitOptions.None))
        {
            string l = raw.TrimEnd('\r');
            if (l.StartsWith("PROJECT: ")) projectPath = l.Substring(9).Trim();
            else if (l.StartsWith("TASK: ")) task = l.Substring(6).Trim();
        }

        List<string> steps = ParsePlanSteps(content);
        if (steps.Count == 0)
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ В файле нет шагов плана.\n");
            return;
        }

        WriteColored(ConsoleColor.DarkGray, "  ◌ План из файла: " + planPath +
                     " (без запроса на планирование)\n");

        string structure = "";
        if (Directory.Exists(projectPath)) structure = ScanDirectory(projectPath, 0);

        RenderPlan(steps, null, projectPath);
        PlanActionMenu(steps, projectPath, task, structure);
    }
}