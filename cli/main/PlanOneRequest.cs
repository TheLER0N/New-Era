// PlanOneRequest.cs — выполнение всего плана за 1 запрос (Guardian-валидация + rollback)
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    static void ExecutePlanOneRequest(List<string> steps, string projectPath, string originalTask, string structure)
    {
        var sb = new StringBuilder(); string effectiveTask = originalTask;
        if (OrchestratorEnabled && !string.IsNullOrWhiteSpace(originalTask)) { try { string enhanced = EnhancePrompt(originalTask); if (!string.IsNullOrWhiteSpace(enhanced)) { effectiveTask = enhanced; WriteColored(ConsoleColor.DarkGray, "  ◌ orchestrator: enhanced\n"); } } catch (Exception orchEx) { WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: bypass (" + orchEx.Message + ")\n"); } }
        sb.Append("Ты — редактор кода. Выполни весь план за один проход.\n");
        sb.Append("Проект: " + projectPath + "\n");
        if (!string.IsNullOrWhiteSpace(effectiveTask)) sb.Append("Задача: " + effectiveTask + "\n");
        if (!string.IsNullOrWhiteSpace(structure)) sb.Append("Структура проекта:\n" + structure + "\n");
        sb.Append("\nПлан:\n"); for (int i = 0; i < steps.Count; i++) sb.Append((i + 1) + ". " + steps[i] + "\n");
        sb.Append("\nПравила:\n");
        sb.Append("- Меняешь файл — верни его ПОЛНОСТЬЮ блоком:\n=== FILE: путь/относительно/проекта ===\nсодержимое\n=== END ===\n");
        sb.Append("- Шаги без файлов опиши одной строкой в начале ответа.\n");
        sb.Append("- Не добавляй ничего сверх плана.\n");
        sb.Append("- Если файл не меняется, не возвращай его.\n");
        string payload = BuildPlanFilePayload(steps, projectPath);
        if (string.IsNullOrEmpty(payload)) {
            if (OrchestratorEnabled) { payload = BuildSelectivePayload(AnalyzeAndSelectFiles(effectiveTask ?? "", projectPath), projectPath); if (string.IsNullOrEmpty(payload)) { WriteColored(ConsoleColor.Yellow, " ⚠ orchestrator: контекст пуст — fallback на локальный скан\n"); payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile); } else { WriteColored(ConsoleColor.DarkGray, " ◌ orchestrator: контекст подобран\n"); } } else { payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile); }
        }
        if (!string.IsNullOrEmpty(payload)) { sb.Append("\nТекущие исходные файлы для правки:\n"); sb.Append(payload); sb.Append("\nВерни изменённые файлы в таких же блоках, сохраняя относительные пути.\n"); }
        else { sb.Append("\nЕсли для правки нужны исходные файлы, которых нет в запросе, не выдумывай: верни одну строку NEED FILES: список путей.\n"); }

        WriteColored(ConsoleColor.DarkGray, " ◌ Выполнение плана за 1 запрос (" + steps.Count + " " + StepsWord(steps.Count) + ")...\n");
        AddHistory("user", "[plan-exec] " + (originalTask ?? "")); StartSpinner("выполнение (1 запрос)");
        string responseText = null;
        try { string raw = PostMessage(sb.ToString(), LastResponseId); responseText = ParseSseAnswer(raw); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  ✖ Ошибка: " + ex.Message + "\n"); return; }
        StopSpinner();
        if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, " ⚠ Пустой ответ.\n"); return; }
        AddHistory("assistant", responseText);
        var files = ParsePlanFileBlocks(responseText);
        if (files.Count == 0) { WriteColored(ConsoleColor.Yellow, " ⚠ Файлов в ответе нет — показываю ответ:\n"); RenderAssistantMessage(responseText); return; }
        Console.WriteLine(); foreach (var kv in files) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(" ▸ " + kv.Key + " (" + kv.Value.Split('\n').Length + " строк)"); } Console.ResetColor(); Console.WriteLine();

        bool guardianBlocked = false;
        if (GuardianEnabled) {
            WriteColored(ConsoleColor.Magenta, " ◆ Guardian: валидация " + files.Count + " файл(ов)...\n");
            string planContext = string.Join("\n", steps.ToArray()); var failedFiles = new List<string>();
            foreach (var kv in files) {
                string baseDir = GetProjectBaseDir(projectPath); string rel = kv.Key.Trim().Trim('"').Replace('/', '\\'); if (rel.StartsWith(".\\")) rel = rel.Substring(2); rel = rel.TrimStart('\\');
                string outPath = Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir ?? BaseDir, rel);
                bool valid = ValidateFileContentWithGuardian(outPath, kv.Value, planContext);
                if (!valid) { failedFiles.Add(kv.Key); WriteColored(ConsoleColor.Red, "    ✖ " + kv.Key + ": FAIL\n"); } else { WriteColored(ConsoleColor.Green, "    ✔ " + kv.Key + ": PASS\n"); }
            }
            if (failedFiles.Count > 0) {
                WriteColored(ConsoleColor.Red, " ✖ Guardian заблокировал " + failedFiles.Count + " файл(ов).\n");
                for (int retry = 1; retry <= GuardianMaxRetries && failedFiles.Count > 0; retry++) {
                    WriteColored(ConsoleColor.Yellow, "  ↻ Retry " + retry + "/" + GuardianMaxRetries + ": запрос исправления у CodeWriter...\n");
                    string fixPrompt = BuildCodeWriterFixPrompt(originalTask, responseText, "Файлы не прошли валидацию: " + string.Join(", ", failedFiles.ToArray()), "Исправь указанные файлы");
                    string fixRaw = null; try { StartSpinner("code writer: fix"); fixRaw = PostCodeWriterMessage(fixPrompt); StopSpinner(); } catch { StopSpinner(); break; }
                    if (string.IsNullOrWhiteSpace(fixRaw)) break;
                    var fixFiles = ParsePlanFileBlocks(fixRaw); if (fixFiles.Count == 0) break;
                    foreach (var fkv in fixFiles) files[fkv.Key] = fkv.Value;
                    var stillFailed = new List<string>();
                    foreach (string fk in failedFiles) { string fContent; if (files.TryGetValue(fk, out fContent)) { string baseDir2 = GetProjectBaseDir(projectPath); string rel2 = fk.Trim().Trim('"').Replace('/', '\\'); if (rel2.StartsWith(".\\")) rel2 = rel2.Substring(2); rel2 = rel2.TrimStart('\\'); string outPath2 = Path.IsPathRooted(rel2) ? rel2 : Path.Combine(baseDir2 ?? BaseDir, rel2); if (ValidateFileContentWithGuardian(outPath2, fContent, planContext)) WriteColored(ConsoleColor.Green, " ✔ " + fk + ": PASS (retry " + retry + ")\n"); else stillFailed.Add(fk); } }
                    failedFiles = stillFailed;
                }
                if (failedFiles.Count > 0) { WriteColored(ConsoleColor.Red, " ✖ " + failedFiles.Count + " файл(ов) не прошли валидацию после " + GuardianMaxRetries + " попыток.\n"); guardianBlocked = true; }
            }
            if (!guardianBlocked) WriteColored(ConsoleColor.Green, " ✔ Guardian: все файлы прошли валидацию\n");
        }

        string confirm;
        if (ArcMode && !guardianBlocked) { WriteColored(ConsoleColor.Green, " ✔ Аркест: авто-применение\n"); confirm = "y"; }
        else if (guardianBlocked) { WriteColored(ConsoleColor.DarkGray, " ◂ Применение заблокировано Guardian.\n"); return; }
        else { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(" ❓ Применить план: " + files.Count + " файл(ов)? [y/N] "); Console.ResetColor(); confirm = Console.ReadLine(); }

        if (confirm != null && confirm.Trim().ToLowerInvariant() == "y") {
            string baseDir = GetProjectBaseDir(projectPath); int written = 0;
            foreach (var kv in files) {
                try {
                    string rel = kv.Key.Trim().Trim('"').Replace('/', '\\'); if (rel.StartsWith(".\\")) rel = rel.Substring(2); rel = rel.TrimStart('\\');
                    string outPath = Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir ?? BaseDir, rel);
                    string dir = Path.GetDirectoryName(outPath); SaveRollbackSnapshot(outPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(outPath, kv.Value, new UTF8Encoding(false));
                    WriteColored(ConsoleColor.Green, "  ✔ " + kv.Key + "\n"); GuardianLog(outPath, "MODIFY", "success"); written++;
                } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  ✖ " + kv.Key + ": " + ex.Message + "\n"); GuardianLog(kv.Key, "MODIFY", "error"); }
            }
            WriteColored(ConsoleColor.Green, "\n✔ План выполнен · 1 запрос · файлов: " + written + "\n");
        } else { WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n"); }
    }
}