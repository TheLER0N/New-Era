// Commands.cs — /say /edit /plan /scan /test /idea /history
// New Era v7.2
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
partial class MainConsole
{
static readonly string[] ContextExtensions = { ".cs", ".bat", ".cmd", ".ps1", ".json", ".xml", ".csproj", ".sln", ".txt", ".cfg", ".ini", ".md" };
// ══════════════════════════════════════════════
//  /idea
// ══════════════════════════════════════════════
const string IdeaSystemPrompt =
    "Ты — архитектурный консультант. ФОРМАТ ОТВЕТА (СТРОГО JSON):\n" +
    "Если нужны уточнения: {\"status\":\"questions\",\"questions\":[{\"id\":1,\"text\":\"Вопрос?\",\"options\":[\"В1\",\"В2\",\"Свой вариант\"]}]}\n" +
    "Если готов: {\"status\":\"idea_ready\",\"idea\":\"Полное описание идеи\"}\n" +
    "Максимум 3 раунда. После 3-го ВСЕГДА idea_ready. Отвечай ТОЛЬКО JSON.";

static void HandleIdea(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: idea <папка> [запрос]\n"); return; }
    string[] parsed = ParsePathAndTask(args);
    string path = parsed[0]; string initialRequest = parsed[1];
    string fullPath;
    try { fullPath = Path.GetFullPath(path); } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n"); return; }
    if (!Directory.Exists(fullPath)) { WriteColored(ConsoleColor.Red, "  \u2716 Папка не найдена.\n"); return; }
    if (string.IsNullOrWhiteSpace(initialRequest)) { WriteColored(ConsoleColor.DarkGray, "  \u25CC Опиши идею:\n"); initialRequest = ReadMultiline(); }
    if (string.IsNullOrWhiteSpace(initialRequest)) return;

    WriteColored(ConsoleColor.Magenta, "  \u25C6 IDEA: брейншторм\n");
    string structure = ScanDirectory(fullPath, 0);
    if (structure.Length > 6000) structure = structure.Substring(0, 6000) + "\n... [truncated]";
    string payload = BuildContextPayload(fullPath, 60000, 3000);
    if (string.IsNullOrEmpty(payload)) payload = "(файлы не найдены)";

    var conversationLog = new StringBuilder();
    conversationLog.Append("Запрос: " + initialRequest + "\n");
    int round = 0; const int MaxRounds = 3; string finalIdea = null;

    while (round < MaxRounds && !StopRequested) {
        round++;
        var pb = new StringBuilder();
        pb.Append(IdeaSystemPrompt + "\nСТРУКТУРА:\n" + structure + "\nФАЙЛЫ:\n" + payload + "\nДИАЛОГ:\n" + conversationLog + "\nРаунд " + round + "/" + MaxRounds + ". ");
        if (round >= MaxRounds) pb.Append("Верни idea_ready.\n"); else pb.Append("Если готов — idea_ready. Иначе вопросы.\n");
        
        PauseBeforePrimary("idea"); StartSpinner("idea");
        string responseText = null;
        try { responseText = PostMessageWithRetry(pb.ToString(), LastResponseId); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); return; }
        StopSpinner();
        if (string.IsNullOrWhiteSpace(responseText)) return;
        AddHistory("assistant", responseText);

        IdeaResponse ideaResp = ParseIdeaResponse(responseText);
        if (ideaResp == null) {
            RenderAssistantMessage(responseText);
            WriteColored(ConsoleColor.DarkGray, "\n  \u25CC Правки (пусто = выход):\n");
            string manual = ReadMultiline();
            if (string.IsNullOrWhiteSpace(manual)) { finalIdea = responseText; break; }
            conversationLog.Append("Ответ: " + responseText + "\nПравки: " + manual + "\n"); continue;
        }
        if (ideaResp.status == "idea_ready" && !string.IsNullOrWhiteSpace(ideaResp.idea)) { finalIdea = ideaResp.idea; break; }
        if (ideaResp.status == "questions" && ideaResp.questions != null && ideaResp.questions.Length > 0) {
            conversationLog.Append("Ответ: [вопросы]\n");
            lock (PrintLock) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine("\n  \u256D\u2500 \u25B8 УТОЧНЕНИЯ " + new string('\u2500', 20) + "\u256E"); Console.ResetColor(); }
            for (int qi = 0; qi < ideaResp.questions.Length; qi++) {
                IdeaQuestion q = ideaResp.questions[qi]; if (q == null) continue;
                WriteColored(ConsoleColor.White, "\n  " + (qi + 1) + ". " + q.text + "\n");
                if (q.options != null) for (int oi = 0; oi < q.options.Length; oi++) WriteColored(ConsoleColor.DarkGray, "     [" + (oi + 1) + "] " + q.options[oi] + "\n");
                Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("     \u276F Выбор: "); Console.ResetColor();
                string ans = Console.ReadLine(); if (ans == null) ans = ""; ans = ans.Trim();
                string resolved = ans; int optNum;
                if (int.TryParse(ans, out optNum) && optNum >= 1 && q.options != null && optNum <= q.options.Length) resolved = q.options[optNum - 1];
                if (string.IsNullOrEmpty(resolved)) resolved = "(пропущено)";
                WriteColored(ConsoleColor.Green, "     \u2714 " + resolved + "\n");
                conversationLog.Append("Q: " + q.text + "\nA: " + resolved + "\n");
            }
            lock (PrintLock) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F\n"); Console.ResetColor(); }
        } else { finalIdea = !string.IsNullOrWhiteSpace(ideaResp.idea) ? ideaResp.idea : responseText; break; }
    }
    if (string.IsNullOrWhiteSpace(finalIdea)) return;
    string ideasFile = Path.Combine(fullPath, "ideas.md");
    try {
        var sb = new StringBuilder();
        if (File.Exists(ideasFile)) { string ex = ReadTextAuto(ideasFile); sb.Append(ex); if (!ex.EndsWith("\n")) sb.Append("\n"); }
        sb.Append("\n---\n## \u2728 Идея \u00B7 " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "\n\n" + finalIdea + "\n");
        File.WriteAllText(ideasFile, sb.ToString(), new UTF8Encoding(false));
        WriteColored(ConsoleColor.Green, "  \u2714 Сохранено: " + ideasFile + "\n");
    } catch {}
    RenderAssistantMessage(finalIdea);
    lock (PrintLock) {
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("\n  \u256D\u2500 \u25C6 ДЕЙСТВИЯ " + new string('\u2500', 30) + "\u256E"); Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  \u2502   [1] \u2708 Реализовать через /plan");
        Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  \u2502   [2] \u2692 Реализовать через /edit");
        Console.ForegroundColor = ConsoleColor.Gray;  Console.WriteLine("  \u2502   [3] \u2708 Выйти");
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F"); Console.ResetColor();
    }
    Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  \u276F "); Console.ResetColor();
    string choice = Console.ReadLine(); if (choice == null) return; choice = choice.Trim();
    if (choice == "1") HandlePlan("plan \"" + fullPath + "\" " + finalIdea.Replace("\n", " "));
    else if (choice == "2") HandleEdit("edit \"" + fullPath + "\" " + finalIdea.Replace("\n", " "));
}

static IdeaResponse ParseIdeaResponse(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    string cleaned = StripMarkdownFences(raw).Trim();
    int jsonStart = cleaned.IndexOf('{'); int jsonEnd = cleaned.LastIndexOf('}');
    if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart) return null;
    string json = cleaned.Substring(jsonStart, jsonEnd - jsonStart + 1);
    try {
        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var obj = ser.DeserializeObject(json) as Dictionary<string, object>;
        if (obj == null) return null;
        var result = new IdeaResponse();
        if (obj.ContainsKey("status")) result.status = obj["status"] as string;
        if (obj.ContainsKey("idea")) result.idea = obj["idea"] as string;
        if (obj.ContainsKey("questions")) {
            object[] qArr = obj["questions"] as object[];
            if (qArr != null) {
                var questions = new List<IdeaQuestion>();
                foreach (object qObj in qArr) {
                    var qDict = qObj as Dictionary<string, object>; if (qDict == null) continue;
                    var q = new IdeaQuestion();
                    if (qDict.ContainsKey("id")) { object idVal = qDict["id"]; if (idVal is int) q.id = (int)idVal; else if (idVal is double) q.id = (int)(double)idVal; }
                    if (qDict.ContainsKey("text")) q.text = qDict["text"] as string;
                    if (qDict.ContainsKey("options")) {
                        object[] optArr = qDict["options"] as object[];
                        if (optArr != null) { var opts = new List<string>(); foreach (object o in optArr) { string s = o as string; if (!string.IsNullOrEmpty(s)) opts.Add(s); } q.options = opts.ToArray(); }
                    }
                    if (!string.IsNullOrEmpty(q.text)) questions.Add(q);
                }
                result.questions = questions.ToArray();
            }
        }
        return result;
    } catch { return null; }
}

// ══════════════════════════════════════════════
//  SAY
// ══════════════════════════════════════════════
static void Say(string text)
{
    if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId)) { WriteColored(ConsoleColor.Red, "  \u2716 Нет конфигурации.\n"); return; }
    AddHistory("user", text); StartSpinner("отправка");
    string responseText = null;
    try { responseText = PostMessageWithRetry(text, LastResponseId); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); return; }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Пустой ответ.\n"); return; }
    AddHistory("assistant", responseText); RenderAssistantMessage(responseText);
}

// ══════════════════════════════════════════════
//  /edit
// ══════════════════════════════════════════════
static void HandleEdit(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: edit <путь> <задача>\n"); return; }
    string targetPath, rangeStr = null, task = null;
    if (args.StartsWith("\"")) {
        int close = args.IndexOf('"', 1);
        if (close > 0) { targetPath = args.Substring(1, close - 1); string rest = args.Substring(close + 1).Trim(); if (Regex.IsMatch(rest, @"^\d+-\d+\s")) { int sp = rest.IndexOf(' '); rangeStr = sp > 0 ? rest.Substring(0, sp) : rest; task = sp > 0 ? rest.Substring(sp + 1).Trim() : ""; } else task = rest; }
        else { targetPath = args.Substring(1).TrimEnd('"'); task = ""; }
    } else {
        string[] parts = args.Split(new[] { ' ' }, 3); targetPath = parts[0];
        if (parts.Length >= 2) { if (Regex.IsMatch(parts[1], @"^\d+-\d+$")) { rangeStr = parts[1]; task = parts.Length >= 3 ? parts[2].Trim() : ""; } else task = args.Substring(parts[0].Length).Trim(); }
    }
    string fullPath; try { fullPath = Path.GetFullPath(targetPath); } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n"); return; }
    if (string.IsNullOrWhiteSpace(task)) { WriteColored(ConsoleColor.DarkGray, "  \u25CC Введи задачу:\n"); task = ReadMultiline(); }
    if (string.IsNullOrWhiteSpace(task)) return;
    if (Directory.Exists(fullPath)) EditFolderV6(fullPath, task);
    else if (File.Exists(fullPath)) EditFileV6(fullPath, rangeStr, task);
    else WriteColored(ConsoleColor.Red, "  \u2716 Путь не найден.\n");
}

static void EditFileV6(string filePath, string rangeStr, string task)
{
    string projectPath = ResolveProjectDirectory(Path.GetDirectoryName(filePath));
    string fileName = Path.GetFileName(filePath);
    string relPath = MakeRelativePath(projectPath, filePath).Replace('\\', '/');
    bool fileExists = File.Exists(filePath);
    string fileContent = ReadTextAuto(filePath) ?? ""; fileContent = fileContent.Replace("\r\n", "\n").TrimEnd('\r', '\n');
    string action = fileExists ? "MODIFY" : "CREATE";
    WriteColored(ConsoleColor.Magenta, "  \u25C6 v7: edit \u00B7 " + fileName + "\n");
    AddHistory("user", "[edit] " + filePath + " " + task);
    DispatchResult dispatch = DispatchRequest(task, projectPath);
    string enhancedTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : task;
    var sb = new StringBuilder();
    sb.Append("Ты — генератор кода. Файл: " + relPath + "\n");
    if (!string.IsNullOrEmpty(dispatch.ContextSummary)) sb.Append("\nCONTEXT:\n" + dispatch.ContextSummary + "\n");
    sb.Append("\nЗадача: " + enhancedTask + "\n\n=== FILE: " + relPath + " ===\n" + (fileContent.Length > MaxContextTotal ? fileContent.Substring(0, MaxContextTotal) : fileContent) + "\n=== END ===\n");
    sb.Append("\nВерни блок:\nFILE: " + relPath + "\nACTION: " + action + "\nCONTENT:\n...\nEND_FILE\n");
    PauseBeforePrimary("edit"); StartSpinner("v7 edit");
    string responseText = null;
    try { responseText = PostMessageWithRetry(sb.ToString(), LastResponseId); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); return; }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) return;
    AddHistory("assistant", responseText);
    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result != null && !result.IsEmpty) { NormalizeSingleFileOperation(result, filePath, projectPath); ApplyValidatedFiles(result, projectPath, ArcMode); return; }
    WriteColored(ConsoleColor.DarkGray, "  \u25CC fallback: прямая правка\n");
    string[] allLines = fileContent.Split(new[] { "\n" }, StringSplitOptions.None);
    for (int i = 0; i < allLines.Length; i++) allLines[i] = allLines[i].TrimEnd('\r');
    int startLine = 0, endLine = allLines.Length - 1;
    if (rangeStr != null) { string[] rp = rangeStr.Split('-'); int.TryParse(rp[0], out startLine); int.TryParse(rp[1], out endLine); startLine = Math.Max(0, startLine - 1); endLine = Math.Min(allLines.Length - 1, endLine - 1); if (startLine > endLine) { int tmp = startLine; startLine = endLine; endLine = tmp; } }
    string stripped = StripMarkdownFences(responseText);
    string[] newLines = stripped.Split(new[] { "\n" }, StringSplitOptions.None);
    ShowDiff(allLines, startLine, endLine, newLines);
    bool doWrite = ArcMode;
    if (!doWrite) { Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  \u2753 Записать? [y/N] "); Console.ResetColor(); string confirm = Console.ReadLine(); doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y"; }
    if (!doWrite) return;
    try {
        var finalLines = new List<string>();
        for (int i = 0; i < startLine; i++) finalLines.Add(allLines[i]);
        foreach (string nl in newLines) finalLines.Add(nl.TrimEnd('\r'));
        for (int i = endLine + 1; i < allLines.Length; i++) finalLines.Add(allLines[i]);
        string finalContent = string.Join("\n", finalLines.ToArray()); if (!finalContent.EndsWith("\n")) finalContent += "\n";
        SaveRollbackSnapshot(filePath);
        File.WriteAllText(filePath, finalContent, new UTF8Encoding(false));
        WriteColored(ConsoleColor.Green, "  \u2714 Записано: " + filePath + "\n");
        LogChange(filePath, action, "success");
    } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Запись: " + ex.Message + "\n"); LogChange(filePath, action, "error"); }
}

static void EditFolderV6(string folderPath, string task)
{
    WriteColored(ConsoleColor.Magenta, "  \u25C6 v7: edit folder\n");
    AddHistory("user", "[edit-folder] " + folderPath + " " + task);
    DispatchResult dispatch = DispatchRequest(task, folderPath);
    string effectiveTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : task;
    string structure = ScanDirectory(folderPath, 0);
    var sb = new StringBuilder();
    sb.Append("Ты — генератор кода. Папка: " + folderPath + "\n");
    if (!string.IsNullOrWhiteSpace(dispatch.ContextSummary)) sb.Append("\nCONTEXT:\n" + dispatch.ContextSummary + "\n");
    sb.Append("Задача: " + effectiveTask + "\n");
    if (!string.IsNullOrEmpty(structure)) sb.Append("Структура:\n" + structure + "\n");
    string payload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0) payload = BuildSelectivePayload(dispatch.SelectedFiles, folderPath);
    if (string.IsNullOrEmpty(payload)) payload = BuildContextPayload(folderPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(payload)) sb.Append("\nCurrent files:\n" + payload + "\n");
    sb.Append("\nВерни FILE/ACTION/CONTENT/END_FILE.\n");
    PauseBeforePrimary("edit folder"); StartSpinner("v7 edit folder");
    string responseText = null;
    try { responseText = PostMessageWithRetry(sb.ToString(), LastResponseId); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); return; }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) return;
    AddHistory("assistant", responseText);
    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result == null || result.IsEmpty) { RenderAssistantMessage(responseText); return; }
    ApplyValidatedFiles(result, folderPath, ArcMode);
}

// ══════════════════════════════════════════════
//  /plan
// ══════════════════════════════════════════════
static void HandlePlan(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: plan <путь> <задача>\n"); return; }
    if (args == "run" || args.StartsWith("run ")) { RunSavedPlan(args.Length > 4 ? args.Substring(4).Trim() : ""); return; }
    string[] parsed = ParsePathAndTask(args); string path = parsed[0], task = parsed[1];
    if (string.IsNullOrWhiteSpace(task)) { WriteColored(ConsoleColor.DarkGray, "  \u25CC Введи задачу:\n"); task = ReadMultiline(); }
    if (string.IsNullOrWhiteSpace(task)) return;
    string fullPath; try { fullPath = Path.GetFullPath(path); } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n"); return; }
    string structure = "";
    if (Directory.Exists(fullPath)) structure = ScanDirectory(fullPath, 0);
    else if (File.Exists(fullPath)) structure = "FILE: " + fullPath;
    else { WriteColored(ConsoleColor.Red, "  \u2716 Путь не найден.\n"); return; }
    DispatchResult dispatch = DispatchRequest(task, fullPath);
    string effectiveTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : task;
    string prompt = "Составь план. Задача: " + effectiveTask + "\nСтруктура:\n" + structure + "\nФормат: N. [ДЕЙСТВИЕ] Файл — описание\n";
    string codePayload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0) codePayload = BuildSelectivePayload(dispatch.SelectedFiles, fullPath);
    if (string.IsNullOrEmpty(codePayload)) codePayload = BuildContextPayload(fullPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(codePayload)) prompt += "\nCurrent files:\n" + codePayload;
    PauseBeforePrimary("plan"); AddHistory("user", "[plan] " + path + " " + task); StartSpinner("план");
    string responseText = null;
    try { responseText = PostMessageWithRetry(prompt, LastResponseId); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); return; }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) return;
    AddHistory("assistant", responseText);
    List<string> steps = ParsePlanSteps(responseText);
    RenderPlan(steps, responseText, fullPath);
    if (steps.Count > 0) PlanActionMenu(steps, fullPath, task, structure);
}

static void PlanActionMenu(List<string> steps, string projectPath, string task, string structure)
{
    lock (PrintLock) {
        int winW; try { winW = Console.WindowWidth; } catch { winW = 80; } if (winW < 44) winW = 44;
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u256D\u2500 \u25C6 ДЕЙСТВИЯ " + new string('\u2500', Math.Max(1, winW - 16)) + "\u256E"); Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  \u2502   [1] Выполнить пошагово");
        Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  \u2502   [2] Пошагово \u00B7 авто");
        Console.ForegroundColor = ConsoleColor.Cyan;  Console.WriteLine("  \u2502   [3] Всё за 1 запрос");
        Console.ForegroundColor = ConsoleColor.Gray;  Console.WriteLine("  \u2502   [4] Сохранить в plan.txt");
        Console.ForegroundColor = ConsoleColor.Gray;  Console.WriteLine("  \u2502   [5] Отмена");
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u2570" + new string('\u2500', winW - 4) + "\u256F"); Console.ResetColor();
    }
    Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("  \u276F "); Console.ResetColor();
    string choice = Console.ReadLine(); if (choice == null) return; choice = choice.Trim();
    if (choice == "1") ExecutePlan(steps, projectPath, task, false);
    else if (choice == "2") ExecutePlan(steps, projectPath, task, true);
    else if (choice == "3") ExecutePlanOneRequest(steps, projectPath, task, structure);
    else if (choice == "4") SavePlanToFile(steps, projectPath, task);
}

static void ExecutePlan(List<string> steps, string projectPath, string originalTask, bool autoConfirm)
{
    WriteColored(ConsoleColor.DarkGray, "\n\u25CC Выполнение плана...\n");
    for (int i = 0; i < steps.Count; i++) {
        if (StopRequested) break;
        string step = steps[i];
        WriteColored(ConsoleColor.Cyan, "  \u25B8 Шаг " + (i + 1) + ": "); WriteColored(ConsoleColor.White, step + "\n");
        bool stepSuccess = false;
        for (int attempt = 1; attempt <= PlanMaxRetries; attempt++) {
            if (StopRequested) break;
            if (attempt > 1) { WriteColored(ConsoleColor.Yellow, "    \u21BB Повтор " + attempt + "\n"); Thread.Sleep(PlanRetryDelayMs); }
            try { stepSuccess = ExecutePlanStep(step, projectPath, originalTask); } catch { stepSuccess = false; }
            if (stepSuccess) break;
        }
        LogChange("plan-step-" + (i + 1), "STEP", stepSuccess ? "success" : "error");
        if (!stepSuccess) { WriteColored(ConsoleColor.Red, "\n\u2716 План ОСТАНОВЛЕН.\n"); break; }
        if (!autoConfirm && !ArcMode && i < steps.Count - 1) {
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write(" \u2753 Следующий? [Y/n/q] "); Console.ResetColor();
            string cont = Console.ReadLine(); if (cont != null) { string c = cont.Trim().ToLowerInvariant(); if (c == "q" || c == "n") break; }
        }
    }
    if (!StopRequested) WriteColored(ConsoleColor.Green, "\n\u2714 План завершён.\n");
}

static bool ExecutePlanStep(string step, string projectPath, string originalTask)
{
    string action, stepFile, stepDesc; TryParsePlanStep(step, out action, out stepFile, out stepDesc);
    string targetFile = ResolvePlanFile(stepFile, projectPath);
    string stepTask = !string.IsNullOrWhiteSpace(stepDesc) ? stepDesc : step;
    if (targetFile != null && File.Exists(targetFile) && !IsDeleteAction(action)) SaveRollbackSnapshot(targetFile);
    if (IsDeleteAction(action)) return ExecuteDeleteStep(targetFile ?? stepFile, projectPath, ArcMode);
    if (targetFile != null && IsEditableAction(action)) { EditFileV6(targetFile, null, stepTask); return true; }
    var sb = new StringBuilder();
    DispatchResult dispatch = DispatchRequest(step, projectPath);
    string effectiveStep = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : step;
    sb.Append("Выполни шаг: " + effectiveStep + "\nКонтекст: " + originalTask + "\n");
    string structure = ""; try { if (Directory.Exists(projectPath)) structure = ScanDirectory(projectPath, 0); } catch { }
    if (!string.IsNullOrWhiteSpace(structure)) sb.Append("\nСтруктура:\n" + structure);
    string payload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0) payload = BuildSelectivePayload(dispatch.SelectedFiles, projectPath);
    if (string.IsNullOrEmpty(payload)) payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(payload)) sb.Append("\nCurrent files:\n" + payload);
    sb.Append("\nВерни FILE/ACTION/CONTENT/END_FILE.\n");
    PauseBeforePrimary("plan step"); AddHistory("user", sb.ToString()); StartSpinner("plan step");
    string responseText = null;
    try { responseText = PostMessageWithRetry(sb.ToString(), LastResponseId); } catch { StopSpinner(); return false; }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) return false;
    AddHistory("assistant", responseText);
    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result != null && !result.IsEmpty) return ApplyValidatedFiles(result, projectPath, ArcMode);
    RenderAssistantMessage(responseText); return true;
}

static bool ExecuteDeleteStep(string filePath, string projectPath, bool approved)
{
    if (string.IsNullOrWhiteSpace(filePath)) return false;
    string baseDir = GetProjectBaseDir(projectPath); string relPath = MakeRelativePath(baseDir, filePath); string safePath;
    if (!TryResolveSafeOutputPath(baseDir, relPath, out safePath)) return false;
    if (!File.Exists(safePath)) return true;
    if (!approved) { Console.ForegroundColor = ConsoleColor.Red; Console.Write("  \u2753 Удалить? [y/N] "); Console.ResetColor(); string confirm = Console.ReadLine(); if (confirm == null || confirm.Trim().ToLowerInvariant() != "y") return true; }
    try { SaveRollbackSnapshot(safePath); File.Delete(safePath); WriteColored(ConsoleColor.Red, "  \u2716 DELETE " + safePath + "\n"); return true; } catch { return false; }
}

static void ExecutePlanOneRequest(List<string> steps, string projectPath, string originalTask, string structure)
{
    DispatchResult dispatch = DispatchRequest(originalTask, projectPath);
    string effectiveTask = !string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt) ? dispatch.EnhancedPrompt : originalTask;
    var sb = new StringBuilder();
    sb.Append("Выполни план за один проход. Проект: " + projectPath + "\nЗадача: " + effectiveTask + "\n");
    if (!string.IsNullOrWhiteSpace(structure)) sb.Append("Структура:\n" + structure + "\n");
    sb.Append("\nПлан:\n"); for (int i = 0; i < steps.Count; i++) sb.Append((i + 1) + ". " + steps[i] + "\n");
    sb.Append("\nВерни FILE/ACTION/CONTENT/END_FILE.\n");
    string payload = null;
    if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0) payload = BuildSelectivePayload(dispatch.SelectedFiles, projectPath);
    if (string.IsNullOrEmpty(payload)) payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);
    if (!string.IsNullOrEmpty(payload)) sb.Append("\nТекущие файлы:\n" + payload);
    PauseBeforePrimary("plan"); AddHistory("user", "[plan-exec] " + originalTask); StartSpinner("plan");
    string responseText = null;
    try { responseText = PostMessageWithRetry(sb.ToString(), LastResponseId); } catch { StopSpinner(); return; }
    StopSpinner();
    if (string.IsNullOrWhiteSpace(responseText)) return;
    AddHistory("assistant", responseText);
    CodeWriterResult result = ExtractCodeOrLocal(responseText);
    if (result == null || result.IsEmpty) { RenderAssistantMessage(responseText); return; }
    ApplyValidatedFiles(result, projectPath, ArcMode);
}

static void RunSavedPlan(string planFileArg)
{
    string planPath; try { planPath = Path.GetFullPath(string.IsNullOrEmpty(planFileArg) ? "plan.txt" : planFileArg); } catch { planPath = Path.Combine(BaseDir, "plan.txt"); }
    if (!File.Exists(planPath)) { WriteColored(ConsoleColor.Red, "  \u2716 План не найден.\n"); return; }
    string content = ReadTextAuto(planPath); string projectPath = BaseDir, task = "";
    foreach (string raw in content.Split(new[] { '\n' }, StringSplitOptions.None)) { string l = raw.TrimEnd('\r'); if (l.StartsWith("PROJECT: ")) projectPath = l.Substring(9).Trim(); else if (l.StartsWith("TASK: ")) task = l.Substring(6).Trim(); }
    List<string> steps = ParsePlanSteps(content); if (steps.Count == 0) return;
    string structure = Directory.Exists(projectPath) ? ScanDirectory(projectPath, 0) : "";
    RenderPlan(steps, null, projectPath); PlanActionMenu(steps, projectPath, task, structure);
}

static void SavePlanToFile(List<string> steps, string projectPath, string task)
{
    try {
        string dir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath); if (string.IsNullOrEmpty(dir)) dir = BaseDir;
        string planFile = Path.Combine(dir, "plan.txt");
        var sb = new StringBuilder();
        sb.AppendLine("PLAN \u00B7 " + DateTime.Now.ToString("dd.MM.yyyy HH:mm")); sb.AppendLine("PROJECT: " + projectPath); sb.AppendLine("TASK: " + (task ?? "")); sb.AppendLine(new string('=', 50));
        for (int i = 0; i < steps.Count; i++) sb.AppendLine((i + 1) + ". " + steps[i]);
        File.WriteAllText(planFile, sb.ToString(), new UTF8Encoding(false));
        WriteColored(ConsoleColor.Green, " \u2714 Сохранён: " + planFile + "\n");
    } catch {}
}

// ══════════════════════════════════════════════
//  /scan
// ══════════════════════════════════════════════
static void HandleScan(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Использование: scan <папка>\n"); return; }
    string[] parsedScan = ParsePathAndTask(args); string scanPath = parsedScan[0];
    if (string.IsNullOrWhiteSpace(scanPath)) return;
    string fullPath; try { fullPath = Path.GetFullPath(scanPath); } catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Путь: " + ex.Message + "\n"); return; }
    if (!Directory.Exists(fullPath)) { WriteColored(ConsoleColor.Red, "  \u2716 Папка не найдена.\n"); return; }
    string tree = ScanDirectory(fullPath, 0);
    lock (PrintLock) {
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u256D\u2500 \u25B8 СТРУКТУРА " + new string('\u2500', 30) + "\u256E"); Console.ResetColor();
        foreach (string line in tree.Split('\n')) { Console.ForegroundColor = ConsoleColor.DarkCyan; Console.Write("  \u2502 "); Console.ForegroundColor = ConsoleColor.White; Console.WriteLine(line.TrimEnd('\r')); }
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F"); Console.ResetColor();
    }
}

// ══════════════════════════════════════════════
//  /test
// ══════════════════════════════════════════════
static void HandleTest(string input)
{
    string args = input.Length > 5 ? input.Substring(5).Trim() : "";
    if (string.IsNullOrEmpty(args)) { RunTestQuick(0); return; }
    string lower = args.ToLowerInvariant();
    if (lower == "list" || lower == "ls") { PrintTestList(); return; }
    if (lower == "quick" || lower == "fast") { RunTestQuick(0); return; }
    string[] parts = args.Split(new[] { ' ' }, 2); int num;
    if (int.TryParse(parts[0].Trim(), out num)) { string rest = parts.Length > 1 ? parts[1].Trim() : ""; if (string.IsNullOrEmpty(rest) || rest.ToLowerInvariant() == "quick") RunTestQuick(num); else RunTestCustom(rest, num); return; }
    RunTestCustom(args, 0);
}

static void PrintTestList()
{
    WriteColored(ConsoleColor.DarkGray, "\n── Список ИИ ──\n");
    WriteColored(!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId) ? ConsoleColor.Green : ConsoleColor.DarkGray, "  [1] Primary \u00B7 " + PrimaryModel + "\n");
    WriteColored(IsAi2Configured() ? ConsoleColor.Green : ConsoleColor.DarkGray, "  [2] AI #2   \u00B7 " + GetAi2Model() + "\n");
}

static void RunTestQuick(int n) { RunTestWithMessage("Скажи привет.", n); }
static void RunTestCustom(string t, int n) { RunTestWithMessage(t, n); }

static void RunTestWithMessage(string message, int onlyNumber)
{
    if (onlyNumber == 0 || onlyNumber == 1) {
        if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId)) {
            WriteColored(ConsoleColor.Cyan, "\n\u25B8 Тест #1 \u00B7 Primary\n"); StartSpinner("тест");
            try { string resp = PostMessageWithRetry(message, LastResponseId); StopSpinner(); if (!string.IsNullOrWhiteSpace(resp)) RenderAssistantMessage(resp); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); }
        }
    }
    if (onlyNumber == 0 || onlyNumber == 2) {
        if (IsAi2Configured()) {
            WriteColored(ConsoleColor.Cyan, "\n\u25B8 Тест #2 \u00B7 AI #2\n"); StartSpinner("тест");
            try { string resp = PostRoleMessageWithRetry("AI #2", null, message, GetAi2Model(), GetAi2Api(), GetAi2Token(), ChatId2, PrimaryTimeoutMs, PrimaryReadWriteTimeoutMs); StopSpinner(); if (!string.IsNullOrWhiteSpace(resp)) RenderAssistantMessage(resp); } catch (Exception ex) { StopSpinner(); WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n"); }
        }
    }
}

// ══════════════════════════════════════════════
//  HELPERS
// ══════════════════════════════════════════════
static List<string> ParsePlanSteps(string text)
{
    var steps = new List<string>();
    foreach (string raw in (text ?? "").Split(new[] { "\n" }, StringSplitOptions.None)) {
        string l = raw.TrimEnd('\r').Trim();
        if (Regex.IsMatch(l, @"^\d+[\.\)]\s")) steps.Add(Regex.Replace(l, @"^\d+[\.\)]\s*", ""));
        else if (l.StartsWith("- ") && l.Contains("[")) steps.Add(l.Substring(2));
    }
    return steps;
}

static bool TryParsePlanStep(string step, out string action, out string file, out string desc)
{
    action = null; file = null; desc = null; if (string.IsNullOrWhiteSpace(step)) return false;
    string s = step.Trim(); Match am = Regex.Match(s, @"^\[([^\]]+)\]\s*"); string rest = s;
    if (am.Success) { action = am.Groups[1].Value.Trim(); rest = s.Substring(am.Length); }
    Match m = Regex.Match(rest, @"^(.+?)\s*[\u2014\u2013]\s*(.*)$");
    if (!m.Success) m = Regex.Match(rest, @"^(.+?)\s+-\s+(.*)$");
    if (m.Success) { file = m.Groups[1].Value.Trim(); desc = m.Groups[2].Value.Trim(); } else desc = rest.Trim();
    if (string.IsNullOrEmpty(action)) action = InferPlanAction(step); return true;
}

static string InferPlanAction(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return null; string t = text.ToUpperInvariant();
    if (t.Contains("УДАЛ") || t.Contains("DELETE")) return "DELETE";
    if (t.Contains("СОЗДА") || t.Contains("CREATE")) return "CREATE";
    if (t.Contains("ИЗУЧ") || t.Contains("READ")) return "READ";
    if (t.Contains("ОБНОВ") || t.Contains("MODIFY")) return "MODIFY";
    return null;
}

static bool IsDeleteAction(string action) { return !string.IsNullOrWhiteSpace(action) && action.ToUpperInvariant().Contains("DELETE"); }
static bool IsEditableAction(string action)
{
    if (string.IsNullOrWhiteSpace(action)) return false; string a = action.ToUpperInvariant();
    return a.Contains("MODIFY") || a.Contains("CREATE") || a.Contains("EDIT") || a.Contains("UPDATE");
}

static string ResolvePlanFile(string file, string projectPath)
{
    if (string.IsNullOrWhiteSpace(file)) return null; string baseDir = GetProjectBaseDir(projectPath);
    string rel = file.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    try { return Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir, rel)); } catch { return null; }
}

static string GetProjectBaseDir(string projectPath)
{
    string baseDir = null; try { baseDir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath); } catch { }
    return string.IsNullOrEmpty(baseDir) ? BaseDir : baseDir;
}

static string[] ParsePathAndTask(string args)
{
    string trimmed = (args ?? "").Trim();
    if (trimmed.StartsWith("\"")) { int close = trimmed.IndexOf('"', 1); if (close > 0) return new[] { trimmed.Substring(1, close - 1), trimmed.Substring(close + 1).Trim() }; return new[] { trimmed.Substring(1).TrimEnd('"'), "" }; }
    string[] words = trimmed.Split(' ');
    return new[] { words[0], words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : "" };
}

static string ReadMultiline()
{
    var sb = new StringBuilder();
    while (true) {
        Console.ForegroundColor = ConsoleColor.DarkGray; Console.Write("  > "); Console.ResetColor();
        string line; try { line = Console.ReadLine(); } catch { break; }
        if (line == null || line.Trim().Length == 0) break;
        if (sb.Length > 0) sb.Append("\n"); sb.Append(line);
    }
    return sb.ToString();
}

static string BuildContextPayload(string path, int maxTotal, int maxFile)
{
    if (string.IsNullOrEmpty(path)) return null;
    var files = new List<string>(); try { if (File.Exists(path)) files.Add(path); else if (Directory.Exists(path)) CollectContextFiles(path, files); } catch { return null; }
    if (files.Count == 0) return null; files.Sort(StringComparer.OrdinalIgnoreCase);
    string baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path); if (string.IsNullOrEmpty(baseDir)) baseDir = BaseDir;
    var sb = new StringBuilder(); long total = 0; int included = 0, skipped = 0;
    foreach (string full in files) {
        string name = Path.GetFileName(full); string rel = MakeRelativePath(baseDir, full);
        if (IsExcludedContextFile(rel, name)) { skipped++; continue; }
        string body; try { body = ReadTextAuto(full); } catch { body = ""; }
        body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n'); bool truncated = false;
        if (maxFile > 0 && body.Length > maxFile) { body = body.Substring(0, maxFile); truncated = true; }
        long blockLen = (long)body.Length + rel.Length + 40; if (maxTotal > 0 && total + blockLen > maxTotal) { skipped++; continue; }
        total += blockLen; included++;
        sb.Append("\n=== FILE: " + rel + " ===\n" + body + "\n"); if (truncated) sb.Append("// [truncated]\n"); sb.Append("=== END ===\n");
    }
    return included == 0 ? null : sb.ToString();
}

static void CollectContextFiles(string path, List<string> files)
{
    var stack = new Stack<string>(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); stack.Push(path);
    while (stack.Count > 0) {
        string current = stack.Pop(); string fullCurrent; try { fullCurrent = Path.GetFullPath(current); } catch { continue; }
        if (!visited.Add(fullCurrent)) continue;
        try {
            foreach (string f in Directory.GetFiles(current)) { string name = Path.GetFileName(f); if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue; string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant(); bool ok = false; foreach (string ce in ContextExtensions) if (ce == ext) { ok = true; break; } if (ok) files.Add(f); }
            foreach (string d in Directory.GetDirectories(current)) { string name = Path.GetFileName(d); if (!IsExcludedContextDir(name)) stack.Push(d); }
        } catch { }
    }
}

static bool IsExcludedContextDir(string name)
{
    if (string.IsNullOrEmpty(name) || name.StartsWith(".")) return true; string n = name.ToLowerInvariant();
    return n == "bin" || n == "obj" || n == "program_from_the_cli" || n == ".git" || n == ".vs" || n == ".vscode" || n == ".idea" || n == "node_modules";
}

static bool IsExcludedContextFile(string rel, string name)
{
    if (string.IsNullOrEmpty(name)) return true; string n = name.ToLowerInvariant();
    if (n == "qwen_config.txt" || n == "chat_history.dat" || n == "qwen_cursor.txt" || n == "plan.txt" || n == "ideas.md") return true;
    if (n.StartsWith("last_") && n.EndsWith(".json")) return true;
    string r = (rel ?? "").Replace('\\', '/');
    return r.Contains("program_from_the_cli/") || r.Contains("/bin/") || r.Contains("/obj/") || r.Contains("/.git/");
}

static string ScanDirectory(string path, int depth)
{
    if (depth > 6) return ""; var sb = new StringBuilder();
    try {
        string[] dirs = Directory.GetDirectories(path); Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
        string[] files = Directory.GetFiles(path); Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        string indent = new string(' ', depth * 2);
        foreach (string d in dirs) {
            string name = Path.GetFileName(d); if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
            string lower = name.ToLowerInvariant(); if (lower == "bin" || lower == "obj" || lower == "node_modules" || lower == "program_from_the_cli" || lower == ".git" || lower == ".vs") continue;
            sb.Append(indent + "[DIR] " + name + "/\n"); sb.Append(ScanDirectory(d, depth + 1));
        }
        foreach (string f in files) { string name = Path.GetFileName(f); if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue; long size = 0; try { size = new FileInfo(f).Length; } catch { } sb.Append(indent + "  " + name + " (" + size + " B)\n"); }
    } catch { }
    return sb.ToString();
}
}
class IdeaQuestion { public int id; public string text; public string[] options; }
class IdeaResponse { public string status; public IdeaQuestion[] questions; public string idea; }