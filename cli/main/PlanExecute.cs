// PlanExecute.cs — пошаговое выполнение плана: retry engine, READ-шаги, контекст
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

partial class MainConsole
{
    static void ExecutePlan(List<string> steps, string projectPath, string originalTask, bool autoConfirm)
    {
        WriteColored(ConsoleColor.DarkGray, "\n◌ Выполнение плана (" + steps.Count + " " + StepsWord(steps.Count) + ")");
        if (autoConfirm || ArcMode) WriteColored(ConsoleColor.Green, " · АВТО-РЕЖИМ");
        if (GuardianEnabled) WriteColored(ConsoleColor.Magenta, " · ◆ guardian");
        WriteColored(ConsoleColor.DarkGray, "...\n");
        int requests = 0; bool planAborted = false;

        for (int i = 0; i < steps.Count; i++) {
            if (StopRequested) break;
            string step = steps[i];
            WriteColored(ConsoleColor.Cyan, "  ▸ Шаг " + (i + 1) + "/" + steps.Count + ": ");
            WriteColored(ConsoleColor.White, step + "\n");
            string action, stepFile, stepDesc;
            TryParsePlanStep(step, out action, out stepFile, out stepDesc);
            string targetFile = ResolvePlanFile(stepFile, projectPath);
            string stepTask = !string.IsNullOrWhiteSpace(stepDesc) ? stepDesc : step;
            if (targetFile != null && File.Exists(targetFile)) SaveRollbackSnapshot(targetFile);

            bool stepSuccess = false;
            for (int attempt = 1; attempt <= PlanMaxRetries; attempt++) {
                if (StopRequested) break;
                if (attempt > 1) {
                    WriteColored(ConsoleColor.Yellow, "    ↻ Повтор " + attempt + "/" + PlanMaxRetries + " (задержка " + (PlanRetryDelayMs / 1000) + "с)...\n");
                    Thread.Sleep(PlanRetryDelayMs);
                }
                try {
                    if (IsReadAction(action) && targetFile != null && File.Exists(targetFile)) {
                        if (attempt == 1) WriteColored(ConsoleColor.DarkGray, " Чтение файла: " + targetFile + "\n");
                        stepSuccess = ExecuteReadStep(targetFile, stepTask, projectPath, originalTask);
                    } else if (targetFile != null && File.Exists(targetFile) && IsEditableAction(action)) {
                        if (attempt == 1) WriteColored(ConsoleColor.DarkGray, " Файл: " + targetFile + "\n");
                        stepSuccess = HandleEditFile(targetFile, null, stepTask, autoConfirm || ArcMode);
                    } else if (targetFile != null && IsEditableAction(action)) {
                        if (attempt == 1) WriteColored(ConsoleColor.DarkGray, " Файл не найден: " + targetFile + " — создание через Guardian+CodeWriter...\n");
                        stepSuccess = HandleEditFile(targetFile, null, stepTask, autoConfirm || ArcMode);
                    } else if (GuardianEnabled && targetFile != null) {
                        if (attempt == 1) WriteColored(ConsoleColor.DarkGray, " Файл: " + targetFile + " — Guardian+CodeWriter...\n");
                        stepSuccess = HandleEditFile(targetFile, null, stepTask, autoConfirm || ArcMode);
                    } else {
                        if (attempt == 1) WriteColored(ConsoleColor.DarkGray, " Отправка в ИИ с контекстом...\n");
                        stepSuccess = SayStepWithContext(step, projectPath, originalTask);
                    }
                } catch (Exception ex) {
                    WriteColored(ConsoleColor.Red, " ✖ Ошибка (попытка " + attempt + "): " + ex.Message + "\n");
                    stepSuccess = false;
                }
                if (stepSuccess) break;
            }
            requests++;
            string logFile = targetFile ?? stepFile ?? "unknown";
            string logAction = action ?? "MODIFY";
            GuardianLog(logFile, logAction, stepSuccess ? "success" : "error");

            if (!stepSuccess) {
                WriteColored(ConsoleColor.Red, "\n✖ Шаг " + (i + 1) + " не выполнен после " + PlanMaxRetries + " попыток. План ОСТАНОВЛЕН.\n");
                WriteColored(ConsoleColor.DarkGray, " ◌ Выполнено шагов: " + i + " из " + steps.Count + " · запросов: " + requests + "\n");
                planAborted = true; break;
            }
            if (!autoConfirm && !ArcMode && i < steps.Count - 1 && !StopRequested) {
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(" ❓ Следующий шаг? [Y/n/q/a] "); Console.ResetColor();
                string cont = Console.ReadLine();
                if (cont != null) {
                    string c = cont.Trim().ToLowerInvariant();
                    if (c == "q" || c == "n") { WriteColored(ConsoleColor.DarkGray, " ◂ План остановлен.\n"); planAborted = true; break; }
                    if (c == "a") { autoConfirm = true; WriteColored(ConsoleColor.Green, " ✔ Авто-режим включён — дальше без подтверждений.\n"); }
                }
            }
        }
        if (!planAborted && !StopRequested)
            WriteColored(ConsoleColor.Green, "\n✔ План завершён" + (autoConfirm || ArcMode ? " (авто)" : "") + " · запросов: " + requests + "\n");
        if (GuardianChangeLog.Count > 0) {
            WriteColored(ConsoleColor.DarkGray, "\n── Лог Guardian ──\n");
            lock (GuardianChangeLog) { foreach (string logLine in GuardianChangeLog) WriteColored(ConsoleColor.DarkGray, "  " + logLine + "\n"); }
        }
    }

    static bool ExecuteReadStep(string filePath, string stepTask, string projectPath, string originalTask) {
        string fileContent = null;
        try { fileContent = ReadTextAuto(filePath); } catch (Exception ex) { WriteColored(ConsoleColor.Red, " ✖ Не удалось прочитать: " + ex.Message + "\n"); return false; }
        if (string.IsNullOrEmpty(fileContent)) { WriteColored(ConsoleColor.Yellow, " ⚠ Файл пуст: " + filePath + "\n"); return true; }
        string truncatedContent = fileContent;
        if (truncatedContent.Length > MaxContextFile) truncatedContent = truncatedContent.Substring(0, MaxContextFile) + "\n... [truncated]";
        string fileName = Path.GetFileName(filePath);
        if (GuardianEnabled) { WriteColored(ConsoleColor.DarkGray, " ◌ Содержимое передано в Guardian+CodeWriter (" + fileContent.Length + " символов)\n"); return HandleEditFile(filePath, null, stepTask, true); }
        var sb = new StringBuilder();
        sb.Append("Выполни шаг плана: " + stepTask + "\n");
        if (!string.IsNullOrWhiteSpace(originalTask)) sb.Append("Контекст: " + originalTask + "\n");
        sb.Append("\nФайл: " + fileName + " (" + filePath + ")\n");
        sb.Append("Содержимое файла:\n"); sb.Append(truncatedContent);
        sb.Append("\nПроанализируй содержимое и выполни задачу.");
        string promptText = sb.ToString();
        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId)) { WriteColored(ConsoleColor.Red, " ✖ Нет конфигурации.\n"); return false; }
        AddHistory("user", promptText); StartSpinner("анализ файла");
        string responseText = null;
        try { string raw = PostMessage(promptText, LastResponseId); responseText = ParseSseAnswer(raw); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  ✖ Ошибка: " + ex.Message + "\n"); return false; }
        StopSpinner();
        if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, " ⚠ Пустой ответ.\n"); return false; }
        AddHistory("assistant", responseText); RenderAssistantMessage(responseText); return true;
    }

    static bool SayStepWithContext(string step, string projectPath, string originalTask) {
        var sb = new StringBuilder(); string effectiveStep = step;
        if (OrchestratorEnabled) { try { string enhanced = EnhancePrompt(step); if (!string.IsNullOrWhiteSpace(enhanced)) { effectiveStep = enhanced; WriteColored(ConsoleColor.DarkGray, "    ◌ orchestrator: enhanced\n"); } } catch (Exception orchEx) { WriteColored(ConsoleColor.Yellow, "    ⚠ orchestrator: bypass (" + orchEx.Message + ")\n"); } }
        sb.Append("Выполни шаг плана: " + effectiveStep + "\n");
        if (!string.IsNullOrWhiteSpace(originalTask)) sb.Append("Контекст: " + originalTask + "\n");
        string structure = ""; try { if (Directory.Exists(projectPath)) structure = ScanDirectory(projectPath, 0); else if (File.Exists(projectPath)) structure = "FILE: " + projectPath; } catch { }
        if (!string.IsNullOrWhiteSpace(structure)) sb.Append("\nСтруктура проекта:\n" + structure);
        string payload = null;
        if (OrchestratorEnabled) { payload = BuildSelectivePayload(AnalyzeAndSelectFiles(effectiveStep, projectPath), projectPath); if (string.IsNullOrEmpty(payload)) { WriteColored(ConsoleColor.Yellow, " ⚠ orchestrator: контекст пуст — fallback на локальный скан\n"); payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile); } else { WriteColored(ConsoleColor.DarkGray, " ◌ orchestrator: контекст подобран\n"); } } else { payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile); }
        if (!string.IsNullOrEmpty(payload)) sb.Append("\nCurrent source files:\n" + payload);
        string promptText = sb.ToString();
        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId)) { WriteColored(ConsoleColor.Red, " ✖ Нет конфигурации.\n"); return false; }
        AddHistory("user", promptText); StartSpinner("отправка");
        string responseText = null;
        try { string raw = PostMessage(promptText, LastResponseId); responseText = ParseSseAnswer(raw); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  ✖ Ошибка: " + ex.Message + "\n"); return false; }
        StopSpinner();
        if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, " ⚠ Пустой ответ.\n"); return false; }
        AddHistory("assistant", responseText); RenderAssistantMessage(responseText); return true;
    }
}