// V6Pipeline.cs — v6.0 маршруты: plan, edit file, edit folder, plan steps, one-request plan
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  PLAN v6.0
    // ══════════════════════════════════════════════════════════
    static void HandlePlanV6(string fullPath, string task, string structure)
    {
        WriteColored(ConsoleColor.Magenta,
            "  ◆ v6.0: dispatcher plan\n");

        string effectiveTask = task;
        DispatchResult dispatch = null;

        try
        {
            dispatch = DispatchRequest(task, fullPath);

            if (!string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt))
                effectiveTask = dispatch.EnhancedPrompt;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: " + ex.Message + " — bypass\n");
        }

        string prompt =
            "Составь план реализации задачи.\n" +
            "Задача: " + effectiveTask + "\n" +
            "Структура проекта:\n" + structure + "\n" +
            "Верни нумерованный план действий. Формат: N. [ДЕЙСТВИЕ] Файл — описание\n" +
            "Правила:\n" +
            "- Один шаг = один файл (правки одного файла группируй в один шаг).\n" +
            "- Только нужные шаги, без воды.\n" +
            "- Без вступлений и пояснений вне списка.";

        string codePayload = null;

        if (dispatch != null && dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
            codePayload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, fullPath);

        if (string.IsNullOrEmpty(codePayload))
            codePayload = BuildContextPayload(fullPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(codePayload))
        {
            prompt +=
                "\nCurrent source files (use as ground truth):\n" + codePayload +
                "\nIf required files are missing, start with NEED FILES: paths.\n";
        }

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Отправка в ИИ (v6 plan)...\n");

        AddHistory("user", "[plan-v6] " + fullPath + " " + task);

        StartSpinner("v6 план");

        string responseText = null;

        try
        {
            string raw = PostMessage(prompt, LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + "\n");

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");
            return;
        }

        AddHistory("assistant", responseText);

        List<string> steps = ParsePlanSteps(responseText);

        RenderPlan(steps, responseText, fullPath);

        if (steps.Count > 0)
            PlanActionMenu(steps, fullPath, task, structure);
    }

    // ══════════════════════════════════════════════════════════
    //  EDIT FILE v6.0
    // ══════════════════════════════════════════════════════════
    static bool HandleEditFileV6(string filePath, string rangeStr, string task, bool autoConfirm)
    {
        string projectPath = ResolveProjectDirectory(Path.GetDirectoryName(filePath));
        string fileName = Path.GetFileName(filePath);
        string relPath = MakeRelativePath(projectPath, filePath).Replace('\\', '/');

        bool fileExists = File.Exists(filePath);

        string fileContent = ReadTextAuto(filePath) ?? "";
        fileContent = fileContent.Replace("\r\n", "\n").TrimEnd('\r', '\n');

        string action = fileExists ? "MODIFY" : "CREATE";

        WriteColored(ConsoleColor.Magenta,
            "  ◆ v6.0: dispatcher edit · " + fileName + "\n");

        AddHistory("user", "[edit-v6] " + filePath + " " + task);

        DispatchResult dispatch = null;
        string enhancedTask = task;

        try
        {
            dispatch = DispatchRequest(task, projectPath);

            if (!string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt))
                enhancedTask = dispatch.EnhancedPrompt;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: " + ex.Message + " — bypass\n");
        }

        var sb = new StringBuilder();

        sb.Append("Ты — генератор кода. Отредактируй один файл.\n");
        sb.Append("Файл: " + relPath + (fileExists ? "" : " (новый файл)") + "\n");

        if (!string.IsNullOrEmpty(rangeStr))
            sb.Append("Диапазон строк: " + rangeStr + "\n");

        if (dispatch != null && !string.IsNullOrWhiteSpace(dispatch.ContextSummary))
            sb.Append("\nCONTEXT SUMMARY:\n" + dispatch.ContextSummary + "\n");

        sb.Append("\nЗадача: " + enhancedTask + "\n");

        sb.Append("\nCurrent source file:\n=== FILE: " + relPath + " ===\n");

        if (fileContent.Length > 120000)
            sb.Append(fileContent.Substring(0, 120000) + "\n// [truncated]");
        else
            sb.Append(fileContent);

        sb.Append("\n=== END ===\n");

        sb.Append("\nВерни ТОЛЬКО один блок:\n");
        sb.Append("FILE: " + relPath + "\n");
        sb.Append("ACTION: " + action + "\n");
        sb.Append("CONTENT:\n");
        sb.Append("...полное новое содержимое файла...\n");
        sb.Append("END_FILE\n");
        sb.Append("Без пояснений и без markdown.\n");

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Отправка в ИИ (v6 edit: " + fileName + ")...\n");

        StartSpinner("v6 edit");

        string responseText = null;

        try
        {
            string raw = PostMessage(sb.ToString(), LastResponseId);

            try
            {
                File.WriteAllText(DumpFile, raw ?? "", new UTF8Encoding(false));
            }
            catch { }

            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + "\n");

            return false;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");

            return false;
        }

        AddHistory("assistant", responseText);

        CodeWriterResult result = ExtractCodeOrLocal(responseText);

        if (result != null && !result.IsEmpty)
        {
            NormalizeSingleFileOperation(result, filePath, projectPath);
            return ApplyValidatedFiles(result, projectPath, autoConfirm);
        }

        // ── Fallback: прямая правка диапазона, если ИИ не вернул FILE-блоки ──
        WriteColored(ConsoleColor.DarkGray,
            "  ◌ fallback: прямая правка диапазона\n");

        string[] allLines = fileContent.Split(new[] { "\n" }, StringSplitOptions.None);

        for (int li = 0; li < allLines.Length; li++)
            allLines[li] = allLines[li].TrimEnd('\r');

        int startLine = 0;
        int endLine = allLines.Length - 1;

        if (rangeStr != null)
        {
            string[] rangeParts = rangeStr.Split('-');

            int.TryParse(rangeParts[0], out startLine);
            int.TryParse(rangeParts[1], out endLine);

            startLine = Math.Max(0, startLine - 1);
            endLine = Math.Min(allLines.Length - 1, endLine - 1);

            if (startLine > endLine)
            {
                int tmp = startLine;
                startLine = endLine;
                endLine = tmp;
            }
        }

        string stripped = StripCodeFence(responseText);
        string[] newLines = stripped.Split(new[] { "\n" }, StringSplitOptions.None);

        while (newLines.Length > 0 && string.IsNullOrWhiteSpace(newLines[newLines.Length - 1]))
        {
            var tmp = new string[newLines.Length - 1];
            Array.Copy(newLines, tmp, tmp.Length);
            newLines = tmp;
        }

        ShowDiff(allLines, startLine, endLine, newLines);

        bool doWrite;

        if (autoConfirm || ArcMode)
        {
            WriteColored(ConsoleColor.Green,
                "  ✔ Авто-запись (без подтверждения)\n");

            doWrite = true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  ❓ Записать изменения? [y/N] ");
            Console.ResetColor();

            string confirm = Console.ReadLine();
            doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y";
        }

        if (!doWrite)
        {
            WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n");
            return false;
        }

        try
        {
            var finalLines = new List<string>();

            for (int i = 0; i < startLine; i++)
                finalLines.Add(allLines[i]);

            foreach (string nl in newLines)
                finalLines.Add(nl.TrimEnd('\r'));

            for (int i = endLine + 1; i < allLines.Length; i++)
                finalLines.Add(allLines[i]);

            string finalContent = string.Join("\n", finalLines.ToArray());

            if (!finalContent.EndsWith("\n"))
                finalContent += "\n";

            SaveRollbackSnapshot(filePath);
            File.WriteAllText(filePath, finalContent, new UTF8Encoding(false));

            WriteColored(ConsoleColor.Green,
                "  ✔ Записано: " + filePath + " (" + finalContent.Length + " символов)\n");

            LogChange(filePath, action, "success");

            return true;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка записи: " + ex.Message + "\n");

            LogChange(filePath, action, "error");

            return false;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  EDIT FOLDER v6.0
    // ══════════════════════════════════════════════════════════
    static void HandleEditFolderV6(string folderPath, string task)
    {
        WriteColored(ConsoleColor.Magenta,
            "  ◆ v6.0: dispatcher edit folder\n");

        AddHistory("user", "[edit-folder-v6] " + folderPath + " " + task);

        DispatchResult dispatch = null;
        string effectiveTask = task;

        try
        {
            dispatch = DispatchRequest(task, folderPath);

            if (!string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt))
                effectiveTask = dispatch.EnhancedPrompt;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: " + ex.Message + " — bypass\n");
        }

        string structure = ScanDirectory(folderPath, 0);

        var sb = new StringBuilder();

        sb.Append("Ты — генератор кода. Создай/измени файлы в папке.\n");
        sb.Append("Папка: " + folderPath + "\n");

        if (dispatch != null && !string.IsNullOrWhiteSpace(dispatch.ContextSummary))
            sb.Append("\nCONTEXT SUMMARY:\n" + dispatch.ContextSummary + "\n");

        sb.Append("Задача: " + effectiveTask + "\n");

        if (!string.IsNullOrEmpty(structure))
            sb.Append("Структура:\n" + structure + "\n");

        string payload = null;

        if (dispatch != null && dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
            payload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, folderPath);

        if (string.IsNullOrEmpty(payload))
            payload = BuildContextPayload(folderPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(payload))
            sb.Append("\nCurrent source files:\n" + payload + "\n");

        sb.Append("\nВерни файлы блоками FILE/ACTION/CONTENT/END_FILE.\n");
        sb.Append("Каждый блок обязан заканчиваться END_FILE.\n");
        sb.Append("Без пояснений и без markdown.\n");

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Отправка в ИИ (v6 edit folder)...\n");

        StartSpinner("v6 edit folder");

        string responseText = null;

        try
        {
            string raw = PostMessage(sb.ToString(), LastResponseId);

            try
            {
                File.WriteAllText(DumpFile, raw ?? "", new UTF8Encoding(false));
            }
            catch { }

            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ " + ex.Message + "\n");

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");

            return;
        }

        AddHistory("assistant", responseText);

        CodeWriterResult result = ExtractCodeOrLocal(responseText);

        if (result == null || result.IsEmpty)
        {
            RenderAssistantMessage(responseText);
            return;
        }

        ApplyValidatedFiles(result, folderPath, ArcMode);
    }

    // ══════════════════════════════════════════════════════════
    //  PLAN STEP v6.0
    // ══════════════════════════════════════════════════════════
    static bool SayStepWithContextV6(string step, string projectPath, string originalTask)
    {
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
                "    ⚠ dispatcher: bypass (" + ex.Message + ")\n");
        }

        sb.Append("Выполни шаг плана: " + effectiveStep + "\n");

        if (!string.IsNullOrWhiteSpace(originalTask))
            sb.Append("Контекст: " + originalTask + "\n");

        string structure = "";

        try
        {
            if (Directory.Exists(projectPath))
                structure = ScanDirectory(projectPath, 0);
            else if (File.Exists(projectPath))
                structure = "FILE: " + projectPath;
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(structure))
            sb.Append("\nСтруктура проекта:\n" + structure);

        string payload = null;

        if (dispatch != null && dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
            payload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, projectPath);

        if (string.IsNullOrEmpty(payload))
            payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(payload))
            sb.Append("\nCurrent source files:\n" + payload);

        string promptText = sb.ToString();

        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId))
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Нет конфигурации.\n");

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
                "  ✖ Ошибка: " + ex.Message + "\n");

            return false;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");

            return false;
        }

        AddHistory("assistant", responseText);
        RenderAssistantMessage(responseText);

        return true;
    }

    // ══════════════════════════════════════════════════════════
    //  PLAN ONE REQUEST v6.0
    // ══════════════════════════════════════════════════════════
    static void ExecutePlanOneRequestV6(List<string> steps, string projectPath, string originalTask, string structure)
    {
        WriteColored(ConsoleColor.Magenta,
            "  ◆ v6.0: plan one-request\n");

        DispatchResult dispatch = null;
        string effectiveTask = originalTask;

        try
        {
            dispatch = DispatchRequest(originalTask, projectPath);

            if (!string.IsNullOrWhiteSpace(dispatch.EnhancedPrompt))
                effectiveTask = dispatch.EnhancedPrompt;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: bypass (" + ex.Message + ")\n");
        }

        var sb = new StringBuilder();

        sb.Append("Ты — генератор кода. Выполни весь план за один проход.\n");
        sb.Append("Проект: " + projectPath + "\n");

        if (!string.IsNullOrWhiteSpace(effectiveTask))
            sb.Append("Задача: " + effectiveTask + "\n");

        if (!string.IsNullOrWhiteSpace(structure))
            sb.Append("Структура проекта:\n" + structure + "\n");

        sb.Append("\nПлан:\n");

        for (int i = 0; i < steps.Count; i++)
            sb.Append((i + 1) + ". " + steps[i] + "\n");

        sb.Append("\nПравила:\n");
        sb.Append("- Меняешь файл — верни его полностью блоком FILE/ACTION/CONTENT/END_FILE.\n");
        sb.Append("- ACTION: CREATE/MODIFY/DELETE.\n");
        sb.Append("- Каждый блок заканчивай END_FILE.\n");
        sb.Append("- Без пояснений и без markdown.\n");

        string payload = null;

        if (dispatch != null && dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0)
            payload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, projectPath);

        if (string.IsNullOrEmpty(payload))
            payload = BuildPlanFilePayload(steps, projectPath);

        if (string.IsNullOrEmpty(payload))
            payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(payload))
        {
            sb.Append("\nТекущие исходные файлы для правки:\n");
            sb.Append(payload);
            sb.Append("\nВерни изменённые файлы в FILE/ACTION/CONTENT/END_FILE блоках.\n");
        }

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Выполнение плана за 1 запрос (v6, " + steps.Count + " " + StepsWord(steps.Count) + ")...\n");

        AddHistory("user", "[plan-exec-v6] " + (originalTask ?? ""));

        StartSpinner("v6 plan one-request");

        string responseText = null;

        try
        {
            string raw = PostMessage(sb.ToString(), LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + "\n");

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");

            return;
        }

        AddHistory("assistant", responseText);

        CodeWriterResult result = ExtractCodeOrLocal(responseText);

        if (result == null || result.IsEmpty)
        {
            Dictionary<string, string> blocks = ParsePlanFileBlocks(responseText);

            if (blocks.Count > 0)
                result = ConvertLegacyFileBlocks(blocks);
        }

        if (result == null || result.IsEmpty)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Файлов в ответе нет — показываю ответ:\n");

            RenderAssistantMessage(responseText);

            return;
        }

        ApplyValidatedFiles(result, projectPath, ArcMode);
    }

    // ══════════════════════════════════════════════════════════
    //  VALIDATION WRAPPER
    // ══════════════════════════════════════════════════════════
    static bool ApplyValidatedFiles(CodeWriterResult result, string baseDir, bool autoConfirm)
    {
        if (result == null || result.IsEmpty)
            return false;

        string details;

        if (ValidateOperationsViaAI2(result, out details))
        {
            return ApplyGeneratedFiles(result, baseDir, autoConfirm);
        }

        WriteColored(ConsoleColor.Red,
            "  ✖ AI #2 validation: FAIL\n");

        if (!string.IsNullOrWhiteSpace(details))
            RenderAssistantMessage(details);

        if (autoConfirm || ArcMode)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Авто-применение отменено валидацией.\n");

            return false;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  ❓ Применить несмотря на FAIL? [y/N] ");
        Console.ResetColor();

        string confirm = Console.ReadLine();

        if (confirm != null && confirm.Trim().ToLowerInvariant() == "y")
            return ApplyGeneratedFiles(result, baseDir, true);

        WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n");

        return false;
    }
}