// PlanExecute.cs — пошаговое выполнение плана (v6.0 routing, retry engine, READ-шаги)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

partial class MainConsole
{
    static void ExecutePlan(
        List<string> steps,
        string projectPath,
        string originalTask,
        bool autoConfirm)
    {
        string NL = Environment.NewLine;

        WriteColored(ConsoleColor.DarkGray,
            NL + "◌ Выполнение плана (" + steps.Count + " " + StepsWord(steps.Count) + ")");

        if (autoConfirm || ArcMode)
            WriteColored(ConsoleColor.Green, " · АВТО-РЕЖИМ");

        if (DispatcherEnabled)
            WriteColored(ConsoleColor.Magenta, " · ◆ dispatcher");

        WriteColored(ConsoleColor.DarkGray, "..." + NL);

        int requests = 0;
        bool planAborted = false;

        for (int i = 0; i < steps.Count; i++)
        {
            if (StopRequested)
                break;

            string step = steps[i];

            WriteColored(ConsoleColor.Cyan,
                "  ▸ Шаг " + (i + 1) + "/" + steps.Count + ": ");

            WriteColored(ConsoleColor.White, step + NL);

            string action;
            string stepFile;
            string stepDesc;

            TryParsePlanStep(step, out action, out stepFile, out stepDesc);

            string targetFile = ResolvePlanFile(stepFile, projectPath);
            string stepTask = !string.IsNullOrWhiteSpace(stepDesc) ? stepDesc : step;

            if (targetFile != null && File.Exists(targetFile))
                SaveRollbackSnapshot(targetFile);

            bool stepSuccess = false;

            for (int attempt = 1; attempt <= PlanMaxRetries; attempt++)
            {
                if (StopRequested)
                    break;

                if (attempt > 1)
                {
                    WriteColored(ConsoleColor.Yellow,
                        "    ↻ Повтор " + attempt + "/" + PlanMaxRetries +
                        " (задержка " + (PlanRetryDelayMs / 1000) + "с)..." + NL);

                    Thread.Sleep(PlanRetryDelayMs);
                }

                try
                {
                    if (targetFile != null && IsEditableAction(action))
                    {
                        if (attempt == 1)
                        {
                            WriteColored(ConsoleColor.DarkGray,
                                " Файл: " + targetFile +
                                (File.Exists(targetFile) ? "" : " (создание)") + NL);
                        }

                        if (DispatcherEnabled)
                            stepSuccess = HandleEditFileV6(targetFile, null, stepTask, autoConfirm || ArcMode);
                        else
                            stepSuccess = HandleEditFile(targetFile, null, stepTask, autoConfirm || ArcMode);
                    }
                    else if (IsReadAction(action) && targetFile != null && File.Exists(targetFile))
                    {
                        if (attempt == 1)
                        {
                            WriteColored(ConsoleColor.DarkGray,
                                " Чтение файла: " + targetFile + NL);
                        }

                        stepSuccess = ExecuteReadStep(targetFile, stepTask, projectPath, originalTask);
                    }
                    else
                    {
                        if (attempt == 1)
                        {
                            WriteColored(ConsoleColor.DarkGray,
                                " Отправка в ИИ с контекстом..." + NL);
                        }

                        if (DispatcherEnabled)
                            stepSuccess = SayStepWithContextV6(step, projectPath, originalTask);
                        else
                            stepSuccess = SayStepWithContextDirect(step, projectPath, originalTask);
                    }
                }
                catch (Exception ex)
                {
                    WriteColored(ConsoleColor.Red,
                        " ✖ Ошибка (попытка " + attempt + "): " + ex.Message + NL);

                    stepSuccess = false;
                }

                if (stepSuccess)
                    break;
            }

            requests++;

            string logFile = targetFile ?? stepFile ?? "unknown";
            string logAction = action ?? "MODIFY";

            LogChange(logFile, logAction, stepSuccess ? "success" : "error");

            if (!stepSuccess)
            {
                WriteColored(ConsoleColor.Red,
                    NL + "✖ Шаг " + (i + 1) + " не выполнен после " +
                    PlanMaxRetries + " попыток. План ОСТАНОВЛЕН." + NL);

                WriteColored(ConsoleColor.DarkGray,
                    " ◌ Выполнено шагов: " + i + " из " + steps.Count +
                    " · запросов: " + requests + NL);

                planAborted = true;

                break;
            }

            if (!autoConfirm && !ArcMode && i < steps.Count - 1 && !StopRequested)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(" ❓ Следующий шаг? [Y/n/q/a] ");
                Console.ResetColor();

                string cont = Console.ReadLine();

                if (cont != null)
                {
                    string c = cont.Trim().ToLowerInvariant();

                    if (c == "q" || c == "n")
                    {
                        WriteColored(ConsoleColor.DarkGray,
                            " ◂ План остановлен." + NL);

                        planAborted = true;

                        break;
                    }

                    if (c == "a")
                    {
                        autoConfirm = true;

                        WriteColored(ConsoleColor.Green,
                            " ✔ Авто-режим включён — дальше без подтверждений." + NL);
                    }
                }
            }
        }

        if (!planAborted && !StopRequested)
        {
            WriteColored(ConsoleColor.Green,
                NL + "✔ План завершён" +
                (autoConfirm || ArcMode ? " (авто)" : "") +
                " · запросов: " + requests + NL);
        }

        lock (ChangeLog)
        {
            if (ChangeLog.Count > 0)
            {
                WriteColored(ConsoleColor.DarkGray,
                    NL + "── Лог изменений ──" + NL);

                foreach (string logLine in ChangeLog)
                {
                    WriteColored(ConsoleColor.DarkGray,
                        "  " + logLine + NL);
                }
            }
        }
    }

    static bool ExecuteReadStep(
        string filePath,
        string stepTask,
        string projectPath,
        string originalTask)
    {
        string NL = Environment.NewLine;

        string fileContent = null;

        try
        {
            fileContent = ReadTextAuto(filePath);
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red,
                " ✖ Не удалось прочитать: " + ex.Message + NL);

            return false;
        }

        if (string.IsNullOrEmpty(fileContent))
        {
            WriteColored(ConsoleColor.Yellow,
                " ⚠ Файл пуст: " + filePath + NL);

            return true;
        }

        string truncatedContent = fileContent;

        if (truncatedContent.Length > MaxContextFile)
            truncatedContent = truncatedContent.Substring(0, MaxContextFile) + NL + "... [truncated]";

        string fileName = Path.GetFileName(filePath);

        string effectiveStep = stepTask;

        if (DispatcherEnabled && IsAi2Configured())
        {
            try
            {
                string enhanced = EnhancePromptViaAI2(stepTask);

                if (!string.IsNullOrWhiteSpace(enhanced))
                    effectiveStep = enhanced;
            }
            catch
            {
            }
        }

        var sb = new StringBuilder();

        sb.Append("Выполни шаг плана: " + effectiveStep + NL);

        if (!string.IsNullOrWhiteSpace(originalTask))
            sb.Append("Контекст: " + originalTask + NL);

        sb.Append(NL + "Файл: " + fileName + " (" + filePath + ")" + NL);
        sb.Append("Содержимое файла:" + NL);
        sb.Append(truncatedContent);
        sb.Append(NL + "Проанализируй содержимое и выполни задачу.");

        string promptText = sb.ToString();

        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId))
        {
            WriteColored(ConsoleColor.Red,
                " ✖ Нет конфигурации." + NL);

            return false;
        }

        AddHistory("user", promptText);

        StartSpinner("анализ файла");

        string responseText = null;

        try
        {
            string raw = PostMessage(promptText, LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + NL);

            return false;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                " ⚠ Пустой ответ." + NL);

            return false;
        }

        AddHistory("assistant", responseText);

        RenderAssistantMessage(responseText);

        return true;
    }

    static bool SayStepWithContextDirect(
        string step,
        string projectPath,
        string originalTask)
    {
        string NL = Environment.NewLine;

        var sb = new StringBuilder();

        sb.Append("Выполни шаг плана: " + step + NL);

        if (!string.IsNullOrWhiteSpace(originalTask))
            sb.Append("Контекст: " + originalTask + NL);

        string structure = "";

        try
        {
            if (Directory.Exists(projectPath))
                structure = ScanDirectory(projectPath, 0);
            else if (File.Exists(projectPath))
                structure = "FILE: " + projectPath;
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(structure))
            sb.Append(NL + "Структура проекта:" + NL + structure);

        string payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(payload))
            sb.Append(NL + "Current source files:" + NL + payload);

        string promptText = sb.ToString();

        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId))
        {
            WriteColored(ConsoleColor.Red,
                " ✖ Нет конфигурации." + NL);

            return false;
        }

        AddHistory("user", promptText);

        StartSpinner("отправка");

        string responseText = null;

        try
        {
            string raw = PostMessage(promptText, LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + NL);

            return false;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                " ⚠ Пустой ответ." + NL);

            return false;
        }

        AddHistory("assistant", responseText);

        RenderAssistantMessage(responseText);

        return true;
    }
}