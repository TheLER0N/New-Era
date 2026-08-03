// PlanExecute.cs — пошаговое выполнение плана (v6.0 routing, retry engine, READ-шаги, DELETE-шаги)
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

            // Для DELETE-шагов rollback делается непосредственно перед удалением.
            if (targetFile != null && File.Exists(targetFile) && !IsDeleteAction(action))
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
                    if (IsDeleteAction(action))
                    {
                        if (attempt == 1)
                        {
                            WriteColored(ConsoleColor.DarkGray,
                                " Удаление файла: " + (targetFile ?? stepFile ?? "unknown") + NL);
                        }

                        stepSuccess = ExecuteDeleteStep(
                            targetFile ?? stepFile,
                            projectPath,
                            autoConfirm || ArcMode);
                    }
                    else if (targetFile != null && IsEditableAction(action))
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

                        stepSuccess = ExecuteContextStepV6(step, projectPath, originalTask, autoConfirm || ArcMode);
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

    static bool ExecuteDeleteStep(string filePath, string projectPath, bool approved)
    {
        string NL = Environment.NewLine;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            WriteColored(ConsoleColor.Red,
                " ✖ DELETE: не указан файл." + NL);
            return false;
        }

        string baseDir = GetProjectBaseDir(projectPath);

        string fullPath;
        try
        {
            string normalized = filePath
                .Trim()
                .Trim('"')
                .Replace('/', Path.DirectorySeparatorChar);

            fullPath = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(baseDir, normalized));
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red,
                " ✖ DELETE: недопустимый путь: " + ex.Message + NL);
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            WriteColored(ConsoleColor.Red,
                " ✖ DELETE: это папка, удаление папок не поддерживается: " + fullPath + NL);
            return false;
        }

        string relPath = MakeRelativePath(baseDir, fullPath);

        string safePath;
        if (!TryResolveSafeOutputPath(baseDir, relPath, out safePath))
        {
            WriteColored(ConsoleColor.Red,
                " ✖ DELETE: путь вне проекта или недопустимый: " + fullPath + NL);
            return false;
        }

        if (!File.Exists(safePath))
        {
            WriteColored(ConsoleColor.DarkGray,
                " ◌ Файл не существует, пропуск: " + safePath + NL);
            return true;
        }

        // Политика R.6: в обычном режиме удаление только после подтверждения.
        // В авто/arc-режиме подтверждение не требуется, но rollback обязателен.
        if (!approved)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("  ❓ Удалить файл? " + safePath + " [y/N] ");
            Console.ResetColor();

            string confirm = Console.ReadLine();

            if (confirm == null || confirm.Trim().ToLowerInvariant() != "y")
            {
                WriteColored(ConsoleColor.DarkGray,
                    " ◂ Удаление пропущено." + NL);
                return true;
            }
        }

        try
        {
            SaveRollbackSnapshot(safePath);
            File.Delete(safePath);

            WriteColored(ConsoleColor.Red,
                " ✖ DELETE " + safePath + NL);

            return true;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red,
                " ✖ Ошибка удаления: " + ex.Message + NL);
            return false;
        }
    }

    static bool IsDeleteAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        string a = action.ToUpperInvariant();

        if (a.Contains("УДАЛИТЬ") ||
            a.Contains("УДАЛЕНИЕ") ||
            a.Contains("СНЕСИ") ||
            a.Contains("СНЕСТИ") ||
            a.Contains("СТЕРЕТЬ"))
        {
            return true;
        }

        if (a.Contains("DELETE") ||
            a.Contains("REMOVE") ||
            a.Contains("ERASE"))
        {
            return true;
        }

        return false;
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

    static bool ExecuteContextStepV6(
        string step,
        string projectPath,
        string originalTask,
        bool approved)
    {
        string NL = Environment.NewLine;

        var sb = new StringBuilder();

        string effectiveStep = step;
        DispatchResult dispatch = null;

        try
        {
            dispatch = DispatchRequest(step, projectPath);

            if (!string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt))
                effectiveStep = dispatch.EnhancedPrompt;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "    ⚠ dispatcher: bypass (" + ex.Message + ")" + NL);
        }

        sb.Append("Выполни шаг плана: " + effectiveStep + NL);

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

        string payload = null;

        if (dispatch != null && dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
            payload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, projectPath);

        if (string.IsNullOrEmpty(payload))
            payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(payload))
            sb.Append(NL + "Current source files:" + NL + payload);

        sb.Append(NL);
        sb.Append("Если шаг требует создать, изменить или удалить файлы, верни операции блоками:" + NL);
        sb.Append("FILE: относительный/путь" + NL);
        sb.Append("ACTION: CREATE|MODIFY|DELETE" + NL);
        sb.Append("CONTENT:" + NL);
        sb.Append("...полное содержимое файла..." + NL);
        sb.Append("END_FILE" + NL);
        sb.Append("Для ACTION DELETE содержимое не нужно. Каждый блок обязан заканчиваться END_FILE." + NL);
        sb.Append("Без пояснений и без markdown. Если файлы не меняются, просто ответь текстом." + NL);

        string promptText = sb.ToString();

        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId))
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Нет конфигурации." + NL);
            return false;
        }

        AddHistory("user", promptText);

        StartSpinner("v6 step");

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
                "  ⚠ Пустой ответ." + NL);
            return false;
        }

        AddHistory("assistant", responseText);

        CodeWriterResult result = ExtractCodeOrLocal(responseText);

        if (result != null && !result.IsEmpty)
            return ApplyValidatedFiles(result, projectPath, approved);

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