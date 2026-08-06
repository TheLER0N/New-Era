// Commands.cs — /say /edit /plan /scan /test /history
// New Era v7.1 · все вызовы ИИ через retry-слой (до 10 попыток)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
partial class MainConsole
{
static readonly string[] ContextExtensions = { ".cs", ".bat", ".cmd", ".ps1", ".json", ".xml", ".csproj", ".sln", ".txt", ".cfg", ".ini", ".md" };
// ══════════════════════════════════════════════
//  SAY (обычный чат) — retry
// ══════════════════════════════════════════════
static void Say(string text)
{
    if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId)) {
        WriteColored(ConsoleColor.Red, "  \u2716 Нет конфигурации. Заполни qwen_config.txt.\n");
        return;
    }
    AddHistory("user", text);
    StartSpinner("отправка");
    string responseText = null;
    try {
        responseText = PostMessageWithRetry(text, LastResponseId);
    } catch (Exception ex) {
        StopSpinner();
        WriteColored(ConsoleColor.Red, "  \u2716 Ошибка: " + ex.Message + "\n");
        return;
    }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) {
        WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n");
        return;
    }
    AddHistory("assistant", responseText);
    RenderAssistantMessage(responseText);
}

// ══════════════════════════════════════════════
//  /edit
// ══════════════════════════════════════════════
static void HandleEdit(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) {
        WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: edit <файл|папка> [N-M] <задача>\n");
        return;
    }
    string targetPath, rangeStr = null, task = null;
    if (args.StartsWith("\"")) {
        int close = args.IndexOf('"', 1);
        if (close > 0) {
            targetPath = args.Substring(1, close - 1);
            string rest = args.Substring(close + 1).Trim();
            if (Regex.IsMatch(rest, @"^\d+-\d+\s")) {
                int sp = rest.IndexOf(' ');
                rangeStr = sp > 0 ? rest.Substring(0, sp) : rest;
                task = sp > 0 ? rest.Substring(sp + 1).Trim() : "";
            } else task = rest;
        } else { targetPath = args.Substring(1).TrimEnd('"'); task = ""; }
    } else {
        string[] parts = args.Split(new[] { ' ' }, 3);
        targetPath = parts[0];
        if (parts.Length >= 2) {
            if (Regex.IsMatch(parts[1], @"^\d+-\d+$")) {
                rangeStr = parts[1];
                task = parts.Length >= 3 ? parts[2].Trim() : "";
            } else task = args.Substring(parts[0].Length).Trim();
        }
    }
    string fullPath;
    try { fullPath = Path.GetFullPath(targetPath); }
    catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n"); return; }

    if (string.IsNullOrWhiteSpace(task)) {
        WriteColored(ConsoleColor.DarkGray, "  \u25CC Введи задачу (пустая строка = конец):\n");
        task = ReadMultiline();
    }
    if (string.IsNullOrWhiteSpace(task)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустая задача.\n"); return; }

    if (Directory.Exists(fullPath)) EditFolderV6(fullPath, task);
    else if (File.Exists(fullPath)) EditFileV6(fullPath, rangeStr, task);
    else WriteColored(ConsoleColor.Red, "  \u2716 Путь не найден: " + fullPath + "\n");
}

static void EditFileV6(string filePath, string rangeStr, string task)
{
    string projectPath = ResolveProjectDirectory(Path.GetDirectoryName(filePath));
    string fileName = Path.GetFileName(filePath);
    string relPath = MakeRelativePath(projectPath, filePath).Replace('\\', '/');
    bool fileExists = File.Exists(filePath);
    string fileContent = ReadTextAuto(filePath) ?? "";
    fileContent = fileContent.Replace("\r\n", "\n").TrimEnd('\r', '\n');
    string action = fileExists ? "MODIFY" : "CREATE";

    WriteColored(ConsoleColor.Magenta, "  \u25C6 v7: edit \u00B7 " + fileName + "\n");
    AddHistory("user", "[edit] " + filePath + " " + task);

    DispatchResult dispatch = DispatchRequest(task, projectPath);
    string enhancedTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : task;

    var sb = new StringBuilder();
    sb.Append("Ты — генератор кода. Отредактируй один файл.\n");
    sb.Append("Файл: " + relPath + (fileExists ? "" : " (новый)") + "\n");
    if (!string.IsNullOrEmpty(dispatch.ContextSummary))
        sb.Append("\nCONTEXT SUMMARY:\n" + dispatch.ContextSummary + "\n");
    sb.Append("\nЗадача: " + enhancedTask + "\n");
    sb.Append("\nCurrent source file:\n=== FILE: " + relPath + " ===\n");
    sb.Append(fileContent.Length > MaxContextTotal ? fileContent.Substring(0, MaxContextTotal) + "\n// [truncated]" : fileContent);
    sb.Append("\n=== END ===\n");
    sb.Append("\nCONSTRAINTS:\n- Меняй только то, что нужно.\n- Сохраняй стиль и отступы.\n- Не добавляй комментарии-пояснения.\n");
    sb.Append("\nВерни ТОЛЬКО один блок:\nFILE: " + relPath + "\nACTION: " + action + "\nCONTENT:\n...полное содержимое...\nEND_FILE\n");
    sb.Append("Без пояснений и без markdown.\n");

    PauseBeforePrimary("edit");
    WriteColored(ConsoleColor.DarkGray, "  \u25CC Отправка в ИИ...\n");
    StartSpinner("v7 edit");
    string responseText = null;
    try {
        responseText = PostMessageWithRetry(sb.ToString(), LastResponseId);
    } catch (Exception ex) {
        StopSpinner();
        WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
        return;
    }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n"); return; }
    AddHistory("assistant", responseText);

    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result != null && !result.IsEmpty) {
        NormalizeSingleFileOperation(result, filePath, projectPath);
        ApplyValidatedFiles(result, projectPath, ArcMode);
        return;
    }

    // Fallback: прямая правка
    WriteColored(ConsoleColor.DarkGray, "  \u25CC fallback: прямая правка\n");
    string[] allLines = fileContent.Split(new[] { "\n" }, StringSplitOptions.None);
    for (int i = 0; i < allLines.Length; i++) allLines[i] = allLines[i].TrimEnd('\r');
    int startLine = 0, endLine = allLines.Length - 1;
    if (rangeStr != null) {
        string[] rp = rangeStr.Split('-');
        int.TryParse(rp[0], out startLine);
        int.TryParse(rp[1], out endLine);
        startLine = Math.Max(0, startLine - 1);
        endLine = Math.Min(allLines.Length - 1, endLine - 1);
        if (startLine > endLine) { int tmp = startLine; startLine = endLine; endLine = tmp; }
    }
    string stripped = StripMarkdownFences(responseText);
    string[] newLines = stripped.Split(new[] { "\n" }, StringSplitOptions.None);
    while (newLines.Length > 0 && string.IsNullOrWhiteSpace(newLines[newLines.Length - 1])) {
        var tmp = new string[newLines.Length - 1];
        Array.Copy(newLines, tmp, tmp.Length);
        newLines = tmp;
    }

    ShowDiff(allLines, startLine, endLine, newLines);

    bool doWrite;
    if (ArcMode) { WriteColored(ConsoleColor.Green, "  \u2714 Авто-запись\n"); doWrite = true; }
    else {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  \u2753 Записать? [y/N] ");
        Console.ResetColor();
        string confirm = Console.ReadLine();
        doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y";
    }
    if (!doWrite) { WriteColored(ConsoleColor.DarkGray, "  \u25C2 Отменено.\n"); return; }

    try {
        var finalLines = new List<string>();
        for (int i = 0; i < startLine; i++) finalLines.Add(allLines[i]);
        foreach (string nl in newLines) finalLines.Add(nl.TrimEnd('\r'));
        for (int i = endLine + 1; i < allLines.Length; i++) finalLines.Add(allLines[i]);
        string finalContent = string.Join("\n", finalLines.ToArray());
        if (!finalContent.EndsWith("\n")) finalContent += "\n";
        SaveRollbackSnapshot(filePath);
        File.WriteAllText(filePath, finalContent, new UTF8Encoding(false));
        WriteColored(ConsoleColor.Green, "  \u2714 Записано: " + filePath + "\n");
        LogChange(filePath, action, "success");
    } catch (Exception ex) {
        WriteColored(ConsoleColor.Red, "  \u2716 Запись: " + ex.Message + "\n");
        LogChange(filePath, action, "error");
    }
}

static void EditFolderV6(string folderPath, string task)
{
    WriteColored(ConsoleColor.Magenta, "  \u25C6 v7: edit folder\n");
    AddHistory("user", "[edit-folder] " + folderPath + " " + task);
    DispatchResult dispatch = DispatchRequest(task, folderPath);
    string effectiveTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : task;
    string structure = ScanDirectory(folderPath, 0);

    var sb = new StringBuilder();
    sb.Append("Ты — генератор кода. Создай/измени файлы в папке.\n");
    sb.Append("Папка: " + folderPath + "\n");
    if (!string.IsNullOrWhiteSpace(dispatch.ContextSummary))
        sb.Append("\nCONTEXT SUMMARY:\n" + dispatch.ContextSummary + "\n");
    sb.Append("Задача: " + effectiveTask + "\n");
    if (!string.IsNullOrEmpty(structure)) sb.Append("Структура:\n" + structure + "\n");

    string payload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
        payload = BuildSelectivePayload(dispatch.SelectedFiles, folderPath);
    if (string.IsNullOrEmpty(payload))
        payload = BuildContextPayload(folderPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(payload)) sb.Append("\nCurrent source files:\n" + payload + "\n");

    sb.Append("\nПравила:\n- Возвращай ТОЛЬКО изменённые файлы.\n- Верни блоками FILE/ACTION/CONTENT/END_FILE.\n- Без пояснений и без markdown.\n");

    PauseBeforePrimary("edit folder");
    WriteColored(ConsoleColor.DarkGray, "  \u25CC Отправка в ИИ...\n");
    StartSpinner("v7 edit folder");
    string responseText = null;
    try {
        responseText = PostMessageWithRetry(sb.ToString(), LastResponseId);
    } catch (Exception ex) {
        StopSpinner();
        WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
        return;
    }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n"); return; }
    AddHistory("assistant", responseText);

    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result == null || result.IsEmpty) { RenderAssistantMessage(responseText); return; }
    ApplyValidatedFiles(result, folderPath, ArcMode);
}

// ══════════════════════════════════════════════
//  /plan — retry
// ══════════════════════════════════════════════
static void HandlePlan(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) {
        WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: plan <путь> <задача>\n");
        return;
    }
    if (args == "run" || args.StartsWith("run ")) { RunSavedPlan(args.Length > 4 ? args.Substring(4).Trim() : ""); return; }
    
    string[] parsed = ParsePathAndTask(args);
    string path = parsed[0], task = parsed[1];

    if (string.IsNullOrWhiteSpace(task)) {
        WriteColored(ConsoleColor.DarkGray, "  \u25CC Введи задачу:\n");
        task = ReadMultiline();
    }
    if (string.IsNullOrWhiteSpace(task)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустая задача.\n"); return; }
    string fullPath;
    try { fullPath = Path.GetFullPath(path); }
    catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n"); return; }

    string structure = "";
    if (Directory.Exists(fullPath)) structure = ScanDirectory(fullPath, 0);
    else if (File.Exists(fullPath)) structure = "FILE: " + fullPath;
    else { WriteColored(ConsoleColor.Red, "  \u2716 Путь не найден: " + fullPath + "\n"); return; }

    DispatchResult dispatch = DispatchRequest(task, fullPath);
    string effectiveTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : task;

    string prompt = "Составь план реализации задачи.\nЗадача: " + effectiveTask + "\nСтруктура проекта:\n" + structure + "\n" +
        "Верни нумерованный план. Формат: N. [ДЕЙСТВИЕ] Файл — описание\n" +
        "Правила:\n- Один шаг = один файл.\n- Только нужные шаги.\n- Без вступлений.";

    string codePayload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
        codePayload = BuildSelectivePayload(dispatch.SelectedFiles, fullPath);
    if (string.IsNullOrEmpty(codePayload))
        codePayload = BuildContextPayload(fullPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(codePayload))
        prompt += "\nCurrent source files:\n" + codePayload;

    PauseBeforePrimary("plan");
    WriteColored(ConsoleColor.DarkGray, "  \u25CC Отправка в ИИ (план)...\n");
    AddHistory("user", "[plan] " + path + " " + task);
    StartSpinner("план");
    string responseText = null;
    try {
        responseText = PostMessageWithRetry(prompt, LastResponseId);
    } catch (Exception ex) {
        StopSpinner();
        WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
        return;
    }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n"); return; }
    AddHistory("assistant", responseText);

    List<string> steps = ParsePlanSteps(responseText);
    RenderPlan(steps, responseText, fullPath);
    if (steps.Count > 0) PlanActionMenu(steps, fullPath, task, structure);
}

static void PlanActionMenu(List<string> steps, string projectPath, string task, string structure)
{
    lock (PrintLock) {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; }
        if (winW < 44) winW = 44;
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u256D\u2500 \u25C6 ДЕЙСТВИЯ " + new string('\u2500', Math.Max(1, winW - 16)) + "\u256E");
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Green;     Console.WriteLine("  \u2502   [1] Выполнить пошагово");
        Console.ForegroundColor = ConsoleColor.Green;     Console.WriteLine("  \u2502   [2] Пошагово \u00B7 авто");
        Console.ForegroundColor = ConsoleColor.Cyan;      Console.WriteLine("  \u2502   [3] Всё за 1 запрос");
        Console.ForegroundColor = ConsoleColor.Gray;      Console.WriteLine("  \u2502   [4] Сохранить в plan.txt");
        Console.ForegroundColor = ConsoleColor.Gray;      Console.WriteLine("  \u2502   [5] Отмена");
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F");
        Console.ResetColor();
    }
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("  \u276F ");
    Console.ResetColor();
    string choice = Console.ReadLine();
    if (choice == null) return;
    choice = choice.Trim();
    if (choice == "1") ExecutePlan(steps, projectPath, task, false);
    else if (choice == "2") ExecutePlan(steps, projectPath, task, true);
    else if (choice == "3") ExecutePlanOneRequest(steps, projectPath, task, structure);
    else if (choice == "4") SavePlanToFile(steps, projectPath, task);
    else WriteColored(ConsoleColor.DarkGray, "  \u25C2 Отменено.\n");
}

static void ExecutePlan(List<string> steps, string projectPath, string originalTask, bool autoConfirm)
{
    WriteColored(ConsoleColor.DarkGray, "\n\u25CC Выполнение плана (" + steps.Count + " шагов)...\n");
    for (int i = 0; i < steps.Count; i++) {
        if (StopRequested) break;
        string step = steps[i];
        WriteColored(ConsoleColor.Cyan, "  \u25B8 Шаг " + (i + 1) + "/" + steps.Count + ": ");
        WriteColored(ConsoleColor.White, step + "\n");
        bool stepSuccess = false;
        for (int attempt = 1; attempt <= PlanMaxRetries; attempt++) {
            if (StopRequested) break;
            if (attempt > 1) {
                WriteColored(ConsoleColor.Yellow, "    \u21BB Повтор " + attempt + "/" + PlanMaxRetries + "\n");
                Thread.Sleep(PlanRetryDelayMs);
            }
            try { stepSuccess = ExecutePlanStep(step, projectPath, originalTask); }
            catch (Exception ex) {
                WriteColored(ConsoleColor.Red, " \u2716 Ошибка: " + ex.Message + "\n");
                stepSuccess = false;
            }
            if (stepSuccess) break;
        }
        LogChange("plan-step-" + (i + 1), "STEP", stepSuccess ? "success" : "error");
        if (!stepSuccess) {
            WriteColored(ConsoleColor.Red, "\n\u2716 Шаг " + (i + 1) + " не выполнен. План ОСТАНОВЛЕН.\n");
            break;
        }
        if (!autoConfirm && !ArcMode && i < steps.Count - 1 && !StopRequested) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(" \u2753 Следующий шаг? [Y/n/q] ");
            Console.ResetColor();
            string cont = Console.ReadLine();
            if (cont != null) {
                string c = cont.Trim().ToLowerInvariant();
                if (c == "q" || c == "n") { WriteColored(ConsoleColor.DarkGray, " \u25C2 План остановлен.\n"); break; }
            }
        }
    }
    if (!StopRequested) WriteColored(ConsoleColor.Green, "\n\u2714 План завершён.\n");
}

static bool ExecutePlanStep(string step, string projectPath, string originalTask)
{
    string action, stepFile, stepDesc;
    TryParsePlanStep(step, out action, out stepFile, out stepDesc);
    string targetFile = ResolvePlanFile(stepFile, projectPath);
    string stepTask = !string.IsNullOrWhiteSpace(stepDesc) ? stepDesc : step;

    if (targetFile != null && File.Exists(targetFile) && !IsDeleteAction(action))
        SaveRollbackSnapshot(targetFile);

    if (IsDeleteAction(action)) return ExecuteDeleteStep(targetFile ?? stepFile, projectPath, ArcMode);
    if (targetFile != null && IsEditableAction(action)) { EditFileV6(targetFile, null, stepTask); return true; }

    var sb = new StringBuilder();
    DispatchResult dispatch = DispatchRequest(step, projectPath);
    string effectiveStep = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : step;

    sb.Append("Выполни шаг плана: " + effectiveStep + "\n");
    if (!string.IsNullOrWhiteSpace(originalTask)) sb.Append("Контекст: " + originalTask + "\n");

    string structure = "";
    try {
        if (Directory.Exists(projectPath)) structure = ScanDirectory(projectPath, 0);
    } catch { }
    if (!string.IsNullOrWhiteSpace(structure)) sb.Append("\nСтруктура:\n" + structure);

    string payload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
        payload = BuildSelectivePayload(dispatch.SelectedFiles, projectPath);
    if (string.IsNullOrEmpty(payload))
        payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(payload)) sb.Append("\nCurrent source files:\n" + payload);

    sb.Append("\nВерни операции FILE/ACTION/CONTENT/END_FILE если нужны изменения. Иначе ответь текстом.\n");

    PauseBeforePrimary("plan step");
    AddHistory("user", sb.ToString());
    StartSpinner("plan step");
    string responseText = null;
    try {
        responseText = PostMessageWithRetry(sb.ToString(), LastResponseId);
    } catch (Exception ex) {
        StopSpinner();
        WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
        return false;
    }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) return false;
    AddHistory("assistant", responseText);

    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result != null && !result.IsEmpty)
        return ApplyValidatedFiles(result, projectPath, ArcMode);

    RenderAssistantMessage(responseText);
    return true;
}

static bool ExecuteDeleteStep(string filePath, string projectPath, bool approved)
{
    if (string.IsNullOrWhiteSpace(filePath)) return false;
    string baseDir = GetProjectBaseDir(projectPath);
    string relPath = MakeRelativePath(baseDir, filePath);
    string safePath;
    if (!TryResolveSafeOutputPath(baseDir, relPath, out safePath)) return false;
    if (!File.Exists(safePath)) return true;
    if (!approved) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("  \u2753 Удалить? " + safePath + " [y/N] ");
        Console.ResetColor();
        string confirm = Console.ReadLine();
        if (confirm == null || confirm.Trim().ToLowerInvariant() != "y") return true;
    }
    try {
        SaveRollbackSnapshot(safePath);
        File.Delete(safePath);
        WriteColored(ConsoleColor.Red, "  \u2716 DELETE " + safePath + "\n");
        return true;
    } catch (Exception ex) {
        WriteColored(ConsoleColor.Red, "  \u2716 Ошибка удаления: " + ex.Message + "\n");
        return false;
    }
}

static void ExecutePlanOneRequest(List<string> steps, string projectPath, string originalTask, string structure)
{
    DispatchResult dispatch = DispatchRequest(originalTask, projectPath);
    string effectiveTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : originalTask;

    var sb = new StringBuilder();
    sb.Append("Ты — генератор кода. Выполни весь план за один проход.\n");
    sb.Append("Проект: " + projectPath + "\n");
    if (!string.IsNullOrWhiteSpace(effectiveTask)) sb.Append("Задача: " + effectiveTask + "\n");
    if (!string.IsNullOrWhiteSpace(structure)) sb.Append("Структура:\n" + structure + "\n");
    sb.Append("\nПлан:\n");
    for (int i = 0; i < steps.Count; i++) sb.Append((i + 1) + ". " + steps[i] + "\n");
    sb.Append("\nВерни изменённые файлы блоками FILE/ACTION/CONTENT/END_FILE. Без пояснений.\n");

    string payload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
        payload = BuildSelectivePayload(dispatch.SelectedFiles, projectPath);
    if (string.IsNullOrEmpty(payload))
        payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(payload)) sb.Append("\nТекущие файлы:\n" + payload);

    PauseBeforePrimary("plan one-request");
    WriteColored(ConsoleColor.DarkGray, "  \u25CC Выполнение плана за 1 запрос...\n");
    AddHistory("user", "[plan-exec] " + (originalTask ?? ""));
    StartSpinner("plan one-request");
    string responseText = null;
    try {
        responseText = PostMessageWithRetry(sb.ToString(), LastResponseId);
    } catch (Exception ex) {
        StopSpinner();
        WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
        return;
    }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n"); return; }
    AddHistory("assistant", responseText);

    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result == null || result.IsEmpty) { RenderAssistantMessage(responseText); return; }
    ApplyValidatedFiles(result, projectPath, ArcMode);
}

static void RunSavedPlan(string planFileArg)
{
    string planPath;
    try { planPath = Path.GetFullPath(string.IsNullOrEmpty(planFileArg) ? "plan.txt" : planFileArg); }
    catch { planPath = Path.Combine(BaseDir, "plan.txt"); }
    if (!File.Exists(planPath)) {
        WriteColored(ConsoleColor.Red, "  \u2716 План не найден: " + planPath + "\n");
        return;
    }
    string content = ReadTextAuto(planPath);
    string projectPath = BaseDir, task = "";
    foreach (string raw in content.Split(new[] { '\n' }, StringSplitOptions.None)) {
        string l = raw.TrimEnd('\r');
        if (l.StartsWith("PROJECT: ")) projectPath = l.Substring(9).Trim();
        else if (l.StartsWith("TASK: ")) task = l.Substring(6).Trim();
    }
    List<string> steps = ParsePlanSteps(content);
    if (steps.Count == 0) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Нет шагов.\n"); return; }

    WriteColored(ConsoleColor.DarkGray, "  \u25CC План из файла: " + planPath + "\n");
    string structure = Directory.Exists(projectPath) ? ScanDirectory(projectPath, 0) : "";
    RenderPlan(steps, null, projectPath);
    PlanActionMenu(steps, projectPath, task, structure);
}

static void SavePlanToFile(List<string> steps, string projectPath, string task)
{
    try {
        string dir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(dir)) dir = BaseDir;
        string planFile = Path.Combine(dir, "plan.txt");
        var sb = new StringBuilder();
        sb.AppendLine("PLAN \u00B7 " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        sb.AppendLine("PROJECT: " + projectPath);
        sb.AppendLine("TASK: " + (task ?? ""));
        sb.AppendLine(new string('=', 50));
        for (int i = 0; i < steps.Count; i++) sb.AppendLine((i + 1) + ". " + steps[i]);
        File.WriteAllText(planFile, sb.ToString(), new UTF8Encoding(false));
        WriteColored(ConsoleColor.Green, " \u2714 Сохранён: " + planFile + "\n");
    } catch (Exception ex) {
        WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
    }
}

// ══════════════════════════════════════════════
//  /scan
// ══════════════════════════════════════════════
static void HandleScan(string input)
{
string args = input.Length > 5 ? input.Substring(5).Trim() : "";
if (string.IsNullOrEmpty(args))
{
WriteColored(ConsoleColor.Yellow, " \u26A0 Использование: scan <папка>\n");
return;
}
string path = null;

// Если путь в кавычках — разбираем так же, как /edit и /plan
if (args.StartsWith("\""))
{
    string[] parsed = ParsePathAndTask(args);
    path = parsed[0];
}
else
{
    // Поддержка пути с пробелами без кавычек:
    // ищем существующий каталог по максимальному префиксу строки.
    // Если не находим — используем ParsePathAndTask как fallback.
    string raw = args.Trim().Trim('"');
    string[] tokens = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    string candidate = "";

    for (int i = 0; i < tokens.Length; i++)
    {
        candidate = i == 0 ? tokens[i] : candidate + " " + tokens[i];

        try
        {
            if (Directory.Exists(candidate))
                path = candidate;
        }
        catch { }
    }

    if (string.IsNullOrWhiteSpace(path))
    {
        string[] parsed = ParsePathAndTask(args);
        path = parsed[0];
    }
}

if (string.IsNullOrWhiteSpace(path))
{
    WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: scan <папка>\n");
    return;
}

string fullPath;
try
{
    fullPath = Path.GetFullPath(path);
}
catch (Exception ex)
{
    WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n");
    return;
}

if (!Directory.Exists(fullPath))
{
    WriteColored(ConsoleColor.Red, "  \u2716 Папка не найдена: " + fullPath + "\n");
    return;
}

string tree = ScanDirectory(fullPath, 0);

lock (PrintLock)
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("  \u256D\u2500 \u25B8 СТРУКТУРА " + new string('\u2500', 30) + "\u256E");
    Console.ResetColor();

    foreach (string line in tree.Split('\n'))
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write("  \u2502 ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(line.TrimEnd('\r'));
    }

    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F");
    Console.ResetColor();
}
}

// ══════════════════════════════════════════════
//  /test — retry на обеих ролях
// ══════════════════════════════════════════════
static void HandleTest(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) { RunTestQuick(0); return; }
    string lower = args.ToLowerInvariant();
    if (lower == "list" || lower == "ls") { PrintTestList(); return; }
    if (lower == "quick" || lower == "fast") { RunTestQuick(0); return; }
    string[] parts = args.Split(new[] { ' ' }, 2);
    int num;
    if (int.TryParse(parts[0].Trim(), out num)) {
        string rest = parts.Length > 1 ? parts[1].Trim() : "";
        if (string.IsNullOrEmpty(rest) || rest.ToLowerInvariant() == "quick") RunTestQuick(num);
        else RunTestCustom(rest, num);
        return;
    }
    RunTestCustom(args, 0);
}

static void PrintTestList()
{
    WriteColored(ConsoleColor.DarkGray, "\n── Список ИИ ──\n");
    WriteColored(!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId) ? ConsoleColor.Green : ConsoleColor.DarkGray,
        "  [1] Primary \u00B7 " + PrimaryModel + " \u00B7 генератор\n");
    WriteColored(IsAi2Configured() ? ConsoleColor.Green : ConsoleColor.DarkGray,
        "  [2] AI #2   \u00B7 " + GetAi2Model() + " \u00B7 помощник (enhance/select/extract/compress)\n");
}

static void RunTestQuick(int onlyNumber) { RunTestWithMessage("Скажи привет.", onlyNumber); }
static void RunTestCustom(string text, int onlyNumber) { RunTestWithMessage(text, onlyNumber); }

static void RunTestWithMessage(string message, int onlyNumber)
{
    // Test Primary (retry)
    if (onlyNumber == 0 || onlyNumber == 1) {
        if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId)) {
            WriteColored(ConsoleColor.Cyan, "\n\u25B8 Тест #1 \u00B7 Primary \u00B7 " + PrimaryModel + "\n");
            StartSpinner("тест Primary");
            try {
                string resp = PostMessageWithRetry(message, LastResponseId);
                StopSpinner();
                if (!string.IsNullOrWhiteSpace(resp)) RenderAssistantMessage(resp);
                else WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n");
            } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); }
        } else WriteColored(ConsoleColor.Yellow, "  \u26A0 Primary: не сконфигурирован\n");
    }

    // Test AI #2 (retry)
    if (onlyNumber == 0 || onlyNumber == 2) {
        if (IsAi2Configured()) {
            WriteColored(ConsoleColor.Cyan, "\n\u25B8 Тест #2 \u00B7 AI #2 \u00B7 " + GetAi2Model() + "\n");
            StartSpinner("тест AI #2");
            try {
                string resp = PostRoleMessageWithRetry("AI #2", null, message, 
                    GetAi2Model(), GetAi2Api(), GetAi2Token(), ChatId2, 
                    PrimaryTimeoutMs, PrimaryReadWriteTimeoutMs);
                StopSpinner();
                if (!string.IsNullOrWhiteSpace(resp)) RenderAssistantMessage(resp);
                else WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n");
            } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); }
        } else WriteColored(ConsoleColor.Yellow, "  \u26A0 AI #2: не сконфигурирован\n");
    }
}

// ══════════════════════════════════════════════
//  HELPERS: plan steps, context, multiline
// ══════════════════════════════════════════════
static List<string> ParsePlanSteps(string text)
{
    var steps = new List<string>();
    foreach (string raw in (text ?? "").Split(new[] { "\n" }, StringSplitOptions.None)) {
        string l = raw.TrimEnd('\r').Trim();
        if (Regex.IsMatch(l, @"^\d+[\.\)]\s"))
            steps.Add(Regex.Replace(l, @"^\d+[\.\)]\s*", ""));
        else if (l.StartsWith("- ") && l.Contains("["))
            steps.Add(l.Substring(2));
    }
    return steps;
}

static bool TryParsePlanStep(string step, out string action, out string file, out string desc)
{
    action = null; file = null; desc = null;
    if (string.IsNullOrWhiteSpace(step)) return false;
    string s = step.Trim();
    Match am = Regex.Match(s, @"^\[([^\]]+)\]\s*");
    string rest = s;
    if (am.Success) { action = am.Groups[1].Value.Trim(); rest = s.Substring(am.Length); }
    Match m = Regex.Match(rest, @"^(.+?)\s*[\u2014\u2013]\s*(.*)$");
    if (!m.Success) m = Regex.Match(rest, @"^(.+?)\s+-\s+(.*)$");
    if (m.Success) { file = m.Groups[1].Value.Trim(); desc = m.Groups[2].Value.Trim(); }
    else desc = rest.Trim();
    if (string.IsNullOrEmpty(action)) action = InferPlanAction(step);
    return true;
}

static string InferPlanAction(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return null;
    string t = text.ToUpperInvariant();
    if (t.Contains("УДАЛ") || t.Contains("DELETE") || t.Contains("REMOVE")) return "DELETE";
    if (t.Contains("СОЗДА") || t.Contains("CREATE") || t.Contains("ADD")) return "CREATE";
    if (t.Contains("ИЗУЧ") || t.Contains("ПРОЧИТ") || t.Contains("READ") || t.Contains("ANALYZE")) return "READ";
    if (t.Contains("ОБНОВ") || t.Contains("ИЗМЕН") || t.Contains("UPDATE") || t.Contains("EDIT") || t.Contains("MODIFY")) return "MODIFY";
    return null;
}

static bool IsDeleteAction(string action) { return !string.IsNullOrWhiteSpace(action) && action.ToUpperInvariant().Contains("DELETE"); }

static bool IsEditableAction(string action)
{
    if (string.IsNullOrWhiteSpace(action)) return false;
    string a = action.ToUpperInvariant();
    return a.Contains("MODIFY") || a.Contains("CREATE") || a.Contains("EDIT") || a.Contains("FIX") || a.Contains("UPDATE") ||
           a.Contains("ПРАВКА") || a.Contains("СОЗДАТЬ") || a.Contains("ИЗМЕНИТЬ");
}

static string ResolvePlanFile(string file, string projectPath)
{
    if (string.IsNullOrWhiteSpace(file)) return null;
    string baseDir = GetProjectBaseDir(projectPath);
    string rel = file.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    try { return Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir, rel)); }
    catch { return null; }
}

static string GetProjectBaseDir(string projectPath)
{
    string baseDir = null;
    try { baseDir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath); } catch { }
    return string.IsNullOrEmpty(baseDir) ? BaseDir : baseDir;
}

static string[] ParsePathAndTask(string args)
{
    string trimmed = (args ?? "").Trim();
    if (trimmed.StartsWith("\"")) {
        int close = trimmed.IndexOf('"', 1);
        if (close > 0) return new[] { trimmed.Substring(1, close - 1), trimmed.Substring(close + 1).Trim() };
        return new[] { trimmed.Substring(1).TrimEnd('"'), "" };
    }
    string[] words = trimmed.Split(' ');
    return new[] { words[0], words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : "" };
}

static string ReadMultiline()
{
    var sb = new StringBuilder();
    while (true) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  > ");
        Console.ResetColor();
        string line;
        try { line = Console.ReadLine(); } catch { break; }
        if (line == null || line.Trim().Length == 0) break;
        if (sb.Length > 0) sb.Append("\n");
        sb.Append(line);
    }
    return sb.ToString();
}

// ══════════════════════════════════════════════
//  CONTEXT BUILDER
// ══════════════════════════════════════════════
static string BuildContextPayload(string path, int maxTotal, int maxFile)
{
    if (string.IsNullOrEmpty(path)) return null;
    var files = new List<string>();
    try { if (File.Exists(path)) files.Add(path); else if (Directory.Exists(path)) CollectContextFiles(path, files); } catch { return null; }
    if (files.Count == 0) return null;
    files.Sort(StringComparer.OrdinalIgnoreCase);
    string baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
    if (string.IsNullOrEmpty(baseDir)) baseDir = BaseDir;
    var sb = new StringBuilder();
    long total = 0; int included = 0, skipped = 0;
    foreach (string full in files) {
        string name = Path.GetFileName(full);
        string rel = MakeRelativePath(baseDir, full);
        if (IsExcludedContextFile(rel, name)) { skipped++; continue; }
        string body;
        try { body = ReadTextAuto(full); } catch { body = ""; }
        body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n');
        bool truncated = false;
        if (maxFile > 0 && body.Length > maxFile) { body = body.Substring(0, maxFile); truncated = true; }
        long blockLen = (long)body.Length + rel.Length + 40;
        if (maxTotal > 0 && total + blockLen > maxTotal) { skipped++; continue; }
        total += blockLen; included++;
        sb.Append("\n=== FILE: " + rel + " ===\n");
        sb.Append(body);
        sb.Append("\n");
        if (truncated) sb.Append("// [truncated]\n");
        sb.Append("=== END ===\n");
    }
    if (included == 0) return null;
    return sb.ToString();
}

static void CollectContextFiles(string path, List<string> files)
{
    var stack = new Stack<string>();
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    stack.Push(path);
    while (stack.Count > 0) {
        string current = stack.Pop();
        string fullCurrent;
        try { fullCurrent = Path.GetFullPath(current); } catch { continue; }
        if (!visited.Add(fullCurrent)) continue;
        try {
            foreach (string f in Directory.GetFiles(current)) {
                string name = Path.GetFileName(f);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
                string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant();
                bool ok = false;
                foreach (string ce in ContextExtensions) if (ce == ext) { ok = true; break; }
                if (ok) files.Add(f);
            }
            foreach (string d in Directory.GetDirectories(current)) {
                string name = Path.GetFileName(d);
                if (!IsExcludedContextDir(name)) stack.Push(d);
            }
        } catch { }
    }
}

static bool IsExcludedContextDir(string name)
{
    if (string.IsNullOrEmpty(name) || name.StartsWith(".")) return true;
    string n = name.ToLowerInvariant();
    return n == "bin" || n == "obj" || n == "program_from_the_cli" || n == ".git" ||
           n == ".vs" || n == ".vscode" || n == ".idea" || n == "node_modules";
}

static bool IsExcludedContextFile(string rel, string name)
{
    if (string.IsNullOrEmpty(name)) return true;
    string n = name.ToLowerInvariant();
    if (n == "qwen_config.txt" || n == "chat_history.dat" || n == "qwen_cursor.txt" || n == "plan.txt") return true;
    if (n.StartsWith("last_") && n.EndsWith(".json")) return true;
    if (n.EndsWith("_report.txt")) return true;
    string r = (rel ?? "").Replace('\\', '/');
    if (r.Contains("program_from_the_cli/") || r.Contains("/bin/") || r.Contains("/obj/") || r.Contains("/.git/")) return true;
    return false;
}

static string ScanDirectory(string path, int depth)
{
    if (depth > 6) return "";
    var sb = new StringBuilder();
    try {
        string[] dirs = Directory.GetDirectories(path);
        Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
        string[] files = Directory.GetFiles(path);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        string indent = new string(' ', depth * 2);
        foreach (string d in dirs) {
            string name = Path.GetFileName(d);
            if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
            string lower = name.ToLowerInvariant();
            if (lower == "bin" || lower == "obj" || lower == "node_modules" ||
                lower == "program_from_the_cli" || lower == ".git" || lower == ".vs") continue;
            
            // FIX: \U0001F4C1 не поддерживается в C# 5 и вызывает ошибки. Заменено на [DIR]
            sb.Append(indent + "[DIR] " + name + "/\n");
            sb.Append(ScanDirectory(d, depth + 1));
        }
        foreach (string f in files) {
            string name = Path.GetFileName(f);
            if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
            long size = 0;
            try { size = new FileInfo(f).Length; } catch { }
            sb.Append(indent + "  " + name + " (" + size + " B)\n");
        }
    } catch { }
    return sb.ToString();
}
}