// EditFileTwoLevel.cs — двухуровневое редактирование файла (Guardian + CodeWriter)
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.IO;
using System.Text;

partial class MainConsole
{
    static bool HandleEditFileTwoLevel(string filePath, string rangeStr, string task, string fragment, string[] allLines, int startLine, int endLine, bool autoConfirm) {
        string fileName = Path.GetFileName(filePath);
        WriteColored(ConsoleColor.Magenta, " ◆ Двухуровневое редактирование: " + fileName + "\n");
        WriteColored(ConsoleColor.DarkGray, " ◌ [1/4] Guardian: анализ запроса...\n");
        string guardianPlan = null, acceptance = null, targetFiles = null;
        for (int gAttempt = 1; gAttempt <= GuardianMaxRetries; gAttempt++) {
            if (gAttempt > 1) WriteColored(ConsoleColor.Yellow, " ↻ Guardian: повтор анализа " + gAttempt + "/" + GuardianMaxRetries + "\n");
            try {
                string analysisPrompt = "Task: " + task + "\nFile: " + fileName + (rangeStr != null ? "\nLine range: " + rangeStr : "") + "\nCurrent code:\n" + fragment;
                string rawAnalysis = PostGuardianMessage(GuardianAnalysisPrompt, analysisPrompt);
                GuardianAnalysis analysis = ParseGuardianAnalysis(rawAnalysis);
                if (analysis.IsValid) { guardianPlan = "ENHANCED_TASK: " + analysis.EnhancedTask + "\nTARGET_FILES: " + analysis.TargetFiles + "\nACCEPTANCE: " + analysis.Acceptance; acceptance = analysis.Acceptance; targetFiles = analysis.TargetFiles; break; }
                WriteColored(ConsoleColor.Yellow, " ⚠ Guardian: неполный разбор (нет секций ENHANCED_TASK/TARGET_FILES/ACCEPTANCE)\n");
            } catch (Exception ex) { WriteColored(ConsoleColor.Yellow, "  ⚠ Guardian: " + ex.Message + "\n"); }
        }
        if (guardianPlan == null) { WriteColored(ConsoleColor.Yellow, " ⚠ Guardian не дал валидный разбор после " + GuardianMaxRetries + " попыток — bypass на исходную задачу.\n"); guardianPlan = "ENHANCED_TASK: " + task + "\nTARGET_FILES: " + fileName + " [MODIFY]\nACCEPTANCE: file compiles"; acceptance = "file compiles"; targetFiles = fileName + " [MODIFY]"; }

        WriteColored(ConsoleColor.DarkGray, " ◌ [2/4] CodeWriter: генерация...\n");
        string cwPrompt = BuildCodeWriterEditPrompt(filePath, task, fragment, rangeStr, guardianPlan, acceptance);
        string cwResponse = null;
        try { StartSpinner("code writer"); cwResponse = PostCodeWriterMessage(cwPrompt); StopSpinner(); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  ✖ CodeWriter: " + ex.Message + "\n"); return false; }
        CodeWriterResult cwResult = ParseCodeWriterResponse(cwResponse);
        if (cwResult.IsEmpty) { WriteColored(ConsoleColor.Red, " ✖ CodeWriter: пустой ответ.\n"); return false; }

        string newContent = GetFileContentFromResult(cwResult, fileName); if (newContent == null) newContent = cwResult.Operations[0].Content;
        for (int vAttempt = 1; vAttempt <= GuardianMaxRetries; vAttempt++) {
            WriteColored(ConsoleColor.DarkGray, " ◌ [3/4] Guardian: валидация...\n");
            bool valid = ValidateFileContentWithGuardian(filePath, newContent, guardianPlan);
            if (valid) { WriteColored(ConsoleColor.Green, "  ✔ Guardian: PASS\n"); break; }
            WriteColored(ConsoleColor.Red, "  ✖ Guardian: FAIL\n");
            if (vAttempt >= GuardianMaxRetries) { WriteColored(ConsoleColor.Red, " ✖ Не прошло валидацию после " + GuardianMaxRetries + " попыток.\n"); WriteColored(ConsoleColor.DarkGray, " ◌ Ручное вмешательство: " + filePath + "\n"); WriteColored(ConsoleColor.DarkGray, " ◌ Последний ответ CodeWriter показан ниже:\n"); RenderAssistantMessage(cwResponse ?? ""); return false; }
            WriteColored(ConsoleColor.Yellow, " ↻ CodeWriter: повтор " + (vAttempt + 1) + "/" + GuardianMaxRetries + "\n");
            string fixPrompt = BuildCodeWriterFixPrompt(task, cwResponse, "валидация не пройдена", null, null, acceptance, targetFiles);
            try { StartSpinner("code writer"); cwResponse = PostCodeWriterMessage(fixPrompt); StopSpinner(); } catch { StopSpinner(); return false; }
            cwResult = ParseCodeWriterResponse(cwResponse); newContent = GetFileContentFromResult(cwResult, fileName); if (newContent == null && !cwResult.IsEmpty) newContent = cwResult.Operations[0].Content; if (newContent == null) return false;
        }

        bool doWrite;
        if (autoConfirm || ArcMode) { WriteColored(ConsoleColor.Green, " ✔ Авто-запись (без подтверждения)\n"); doWrite = true; }
        else { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(" ❓ Записать изменения? [y/N] "); Console.ResetColor(); string confirm = Console.ReadLine(); doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y"; }
        if (!doWrite) { WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n"); return false; }
        SaveRollbackSnapshot(filePath);
        try { if (!newContent.EndsWith("\n")) newContent += "\n"; File.WriteAllText(filePath, newContent, new UTF8Encoding(false)); WriteColored(ConsoleColor.Green, " ✔ Записано: " + filePath + " (" + newContent.Length + " символов)\n"); GuardianLog(filePath, "MODIFY", "success"); } catch (Exception ex) { WriteColored(ConsoleColor.Red, " ✖ Ошибка записи: " + ex.Message + "\n"); GuardianLog(filePath, "MODIFY", "error"); return false; }
        PostValidateWrittenFile(filePath, guardianPlan); return true;
    }
    static void PostValidateWrittenFile(string filePath, string planContext) {
        if (!GuardianEnabled) return;
        try { string actual = ReadTextAuto(filePath); bool ok = ValidateFileContentWithGuardian(filePath, actual, planContext); if (ok) WriteColored(ConsoleColor.Green, " ✔ Пост-валидация: PASS\n"); else WriteColored(ConsoleColor.Yellow, " ⚠ Пост-валидация: FAIL (файл записан, но не прошёл проверку)\n"); } catch { }
    }
    static string ResolveEditPath(string input) { if (string.IsNullOrWhiteSpace(input)) return null; try { return Path.GetFullPath(input.Trim().Trim('"')); } catch { return null; } }
}