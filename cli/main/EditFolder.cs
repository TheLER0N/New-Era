// EditFolder.cs — редактирование папки (multi-file create, двухуровневый режим)
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    static void HandleEditFolder(string folderPath, string task) {
        if (GuardianEnabled) { HandleEditFolderTwoLevel(folderPath, task); return; }
        string effectiveTask = task;
        if (OrchestratorEnabled) { try { string enhanced = EnhancePrompt(task); if (!string.IsNullOrWhiteSpace(enhanced)) { effectiveTask = enhanced; WriteColored(ConsoleColor.DarkGray, "  ◌ orchestrator: enhanced\n"); } } catch { } }
        string structure = ScanDirectory(folderPath, 0); string payload = null;
        if (OrchestratorEnabled) payload = BuildSelectivePayload(AnalyzeAndSelectFiles(effectiveTask, folderPath), folderPath);
        if (string.IsNullOrEmpty(payload)) payload = BuildContextPayload(folderPath, 120000, 40000);
        var sb = new StringBuilder();
        sb.Append("Ты — редактор кода. Создай/измени файлы в папке.\n"); sb.Append("Папка: " + folderPath + "\n"); sb.Append("Задача: " + effectiveTask + "\n");
        if (!string.IsNullOrEmpty(structure)) sb.Append("Структура:\n" + structure + "\n");
        if (!string.IsNullOrEmpty(payload)) sb.Append("\nCurrent source files:\n" + payload + "\n");
        sb.Append("\nВерни файлы блоками: === FILE: path === / === END ===\n");
        WriteColored(ConsoleColor.DarkGray, " ◌ Отправка в ИИ (edit folder)...\n");
        AddHistory("user", "[edit-folder] " + folderPath + " " + task); StartSpinner("редактирование папки");
        string responseText = null;
        try { string raw = PostMessage(sb.ToString(), LastResponseId); responseText = ParseSseAnswer(raw); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  ✖ " + ex.Message + "\n"); return; }
        StopSpinner();
        if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, " ⚠ Пустой ответ.\n"); return; }
        AddHistory("assistant", responseText);
        var files = ParseFileBlocks(responseText);
        if (files.Count == 0) { RenderAssistantMessage(responseText); return; }
        Console.WriteLine(); foreach (var kv in files) WriteColored(ConsoleColor.Cyan, " ▸ " + kv.Key + " (" + kv.Value.Split('\n').Length + " строк)\n"); Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(" ❓ Применить " + files.Count + " файл(ов)? [y/N] "); Console.ResetColor();
        string confirm = Console.ReadLine(); if (confirm == null || confirm.Trim().ToLowerInvariant() != "y") { WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n"); return; }
        int written = 0;
        foreach (var kv in files) { try { string rel = kv.Key.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar); string outPath = Path.IsPathRooted(rel) ? rel : Path.Combine(folderPath, rel); string dir = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir); File.WriteAllText(outPath, kv.Value, new UTF8Encoding(false)); WriteColored(ConsoleColor.Green, "  ✔ " + kv.Key + "\n"); written++; } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  ✖ " + kv.Key + ": " + ex.Message + "\n"); } }
        WriteColored(ConsoleColor.Green, "\n✔ Записано файлов: " + written + "\n");
    }
    static void HandleEditFolderTwoLevel(string folderPath, string task) {
        WriteColored(ConsoleColor.Magenta, " ◆ Двухуровневое редактирование папки: " + folderPath + "\n");
        WriteColored(ConsoleColor.DarkGray, "  ◌ [1/4] Guardian: анализ...\n");
        string guardianPlan = null, targetFiles = null;
        for (int g = 1; g <= GuardianMaxRetries; g++) {
            if (g > 1) WriteColored(ConsoleColor.Yellow, " ↻ Guardian: повтор " + g + "/" + GuardianMaxRetries + "\n");
            try { string structure = ScanDirectory(folderPath, 0); string raw = PostGuardianMessage(GuardianAnalysisPrompt, "Task: " + task + "\nFolder: " + folderPath + "\nStructure:\n" + structure); GuardianAnalysis a = ParseGuardianAnalysis(raw); if (a.IsValid) { guardianPlan = "ENHANCED_TASK: " + a.EnhancedTask + "\nTARGET_FILES: " + a.TargetFiles + "\nACCEPTANCE: " + a.Acceptance; targetFiles = a.TargetFiles; break; } WriteColored(ConsoleColor.Yellow, " ⚠ Guardian: неполный разбор\n"); } catch (Exception ex) { WriteColored(ConsoleColor.Yellow, "  ⚠ Guardian: " + ex.Message + "\n"); }
        }
        if (guardianPlan == null) { WriteColored(ConsoleColor.Yellow, " ⚠ Guardian bypass — исходная задача.\n"); guardianPlan = "ENHANCED_TASK: " + task + "\nTARGET_FILES: (all)\nACCEPTANCE: compiles"; }

        WriteColored(ConsoleColor.DarkGray, " ◌ [2/4] CodeWriter: генерация...\n");
        string payload = BuildContextPayload(folderPath, 120000, 40000);
        string cwPrompt = BuildCodeWriterPrompt(task, guardianPlan, payload);
        string cwResponse = null;
        try { StartSpinner("code writer"); cwResponse = PostCodeWriterMessage(cwPrompt); StopSpinner(); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  ✖ " + ex.Message + "\n"); return; }
        CodeWriterResult cwResult = ParseCodeWriterResponse(cwResponse);
        if (cwResult.IsEmpty) { WriteColored(ConsoleColor.Red, " ✖ CodeWriter: пустой ответ.\n"); return; }

        WriteColored(ConsoleColor.DarkGray, " ◌ [3/4] Guardian: валидация " + cwResult.Operations.Count + " файл(ов)...\n");
        bool allPass = true;
        foreach (var op in cwResult.Operations) { string outPath = Path.IsPathRooted(op.Path) ? op.Path : Path.Combine(folderPath, op.Path); bool ok = ValidateFileContentWithGuardian(outPath, op.Content, guardianPlan); if (ok) WriteColored(ConsoleColor.Green, "    ✔ " + op.Path + ": PASS\n"); else { WriteColored(ConsoleColor.Red, "    ✖ " + op.Path + ": FAIL\n"); allPass = false; } }
        if (!allPass) { WriteColored(ConsoleColor.Yellow, " ⚠ Не все файлы прошли валидацию. Показываю ответ:\n"); RenderAssistantMessage(cwResponse ?? ""); return; }

        WriteColored(ConsoleColor.DarkGray, " ◌ [4/4] Применение...\n");
        bool doWrite;
        if (ArcMode) { WriteColored(ConsoleColor.Green, " ✔ Аркест: авто-применение\n"); doWrite = true; }
        else { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(" ❓ Применить " + cwResult.Operations.Count + " файл(ов)? [y/N] "); Console.ResetColor(); string confirm = Console.ReadLine(); doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y"; }
        if (!doWrite) { WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n"); return; }
        int written = 0;
        foreach (var op in cwResult.Operations) { try { string rel = (op.Path ?? "").Replace('/', Path.DirectorySeparatorChar); string outPath = Path.IsPathRooted(rel) ? rel : Path.Combine(folderPath, rel); SaveRollbackSnapshot(outPath); if (op.IsDelete) { if (File.Exists(outPath)) File.Delete(outPath); WriteColored(ConsoleColor.Red, "  ✖ DELETE " + op.Path + "\n"); } else { string dir = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir); string content = op.Content ?? ""; if (!content.EndsWith("\n")) content += "\n"; File.WriteAllText(outPath, content, new UTF8Encoding(false)); WriteColored(ConsoleColor.Green, "  ✔ " + op.Path + "\n"); } GuardianLog(outPath, op.Action ?? "MODIFY", "success"); written++; } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  ✖ " + op.Path + ": " + ex.Message + "\n"); GuardianLog(op.Path, op.Action ?? "MODIFY", "error"); } }
        WriteColored(ConsoleColor.Green, "\n✔ Записано файлов: " + written + "\n");
    }
}