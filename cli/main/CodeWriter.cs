// CodeWriter.cs — классы и методы для двухуровневой системы (CODE_WRITER)
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// Формат ответа CODE_WRITER:
//   FILE: путь/имя
//   ACTION: CREATE|MODIFY|DELETE
//   CONTENT:
//   ...код...
//   END_FILE
//
// v5.2:
//   - CodeWriterSystemPrompt: правило NEED FILES для READ-файлов из TARGET_FILES.
//   - BuildCodeWriterEditPrompt / BuildCodeWriterPrompt: явный блок FILES TO READ/MODIFY/CREATE.
//   - BuildCodeWriterFixPrompt: передаёт TARGET_FILES чтобы retry не терял список файлов.
// v5.1 FIX:
//   - BuildCodeWriterEditPrompt принимает явный план Guardian (guardianPlan)
//     и критерии приёмки (acceptance) — план больше НЕ теряется.
//   - v5.0: парсер использует \n (не AppendLine → \r\n), DELETE без CONTENT,
//     StripCodeWriterFences, логирование, валидация незакрытых блоков.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Операция над файлом (результат работы CODE_WRITER)</summary>
class FileOperation
{
    public string Path;
    public string Action;   // CREATE, MODIFY, DELETE
    public string Content;
    public bool IsCreate { get { return Action != null && Action.ToUpperInvariant() == "CREATE"; } }
    public bool IsModify { get { return Action != null && Action.ToUpperInvariant() == "MODIFY"; } }
    public bool IsDelete { get { return Action != null && Action.ToUpperInvariant() == "DELETE"; } }
}

/// <summary>Результат парсинга ответа CODE_WRITER</summary>
class CodeWriterResult
{
    public string RawText = "";
    public bool PlanConfirmed = false;
    public bool HasValidMarkers = false;
    public List<FileOperation> Operations = new List<FileOperation>();
    public List<string> FilesAffected = new List<string>();
    public List<string> ValidationErrors = new List<string>();
    public bool IsEmpty { get { return Operations.Count == 0; } }
}

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  CODE_WRITER SYSTEM PROMPT
    //  v5.2: добавлено правило NEED FILES для READ-файлов.
    // ══════════════════════════════════════════════════════════
    const string CodeWriterSystemPrompt =
        "You are CODE_WRITER in a two-level code editing system. " +
        "Your role: generate or modify code according to the plan from SYSTEM_GUARDIAN.\n" +
        "Rules:\n" +
        "- Output ONLY file blocks in this exact format:\n" +
        "FILE: relative/path/to/file\n" +
        "ACTION: CREATE|MODIFY|DELETE\n" +
        "CONTENT:\n" +
        "...full file content...\n" +
        "END_FILE\n" +
        "- For MODIFY: return the FULL file content, not a diff.\n" +
        "- For DELETE: CONTENT section is empty.\n" +
        "- No explanations, no markdown fences, no text outside FILE blocks.\n" +
        "- Do NOT wrap code in ``` markers.\n" +
        "- Preserve original formatting and encoding style.\n" +
        "- If multiple files, output multiple FILE blocks sequentially.\n" +
        "- CRITICAL: every FILE block MUST end with END_FILE on its own line.\n" +
        "- If TARGET_FILES lists files to READ, request their content via NEED FILES: paths before generating. " +
        "Do NOT guess the content of files marked as READ.";

    // ══════════════════════════════════════════════════════════
    //  v5.2: извлечение TARGET_FILES из плана Guardian
    // ══════════════════════════════════════════════════════════
    static string ExtractTargetFilesFromPlan(string guardianPlan)
    {
        if (string.IsNullOrWhiteSpace(guardianPlan)) return null;
        string upper = guardianPlan.ToUpperInvariant();
        int tfIdx = upper.IndexOf("TARGET_FILES:");
        if (tfIdx < 0) return null;
        int from = tfIdx + "TARGET_FILES:".Length;
        // Ищем конец секции: следующий маркер или конец строки
        int endIdx = guardianPlan.Length;
        string[] nextMarkers = { "ACCEPTANCE:", "ENHANCED_TASK:", "SUGGESTIONS:" };
        foreach (string marker in nextMarkers)
        {
            int mIdx = upper.IndexOf(marker, from);
            if (mIdx >= 0 && mIdx < endIdx) endIdx = mIdx;
        }
        string section = guardianPlan.Substring(from, endIdx - from).Trim();
        return string.IsNullOrWhiteSpace(section) ? null : section;
    }

    // ══════════════════════════════════════════════════════════
    //  v5.2: форматирование блока FILES TO READ / MODIFY / CREATE
    // ══════════════════════════════════════════════════════════
    static string FormatTargetFilesBlock(string targetFiles)
    {
        if (string.IsNullOrWhiteSpace(targetFiles)) return null;
        var sb = new StringBuilder();
        sb.Append("\nFILES TO READ / MODIFY / CREATE (from SYSTEM_GUARDIAN):\n");
        sb.Append(targetFiles);
        sb.Append("\n");
        sb.Append("For files marked READ: if their content is not provided below, ");
        sb.Append("output NEED FILES: <paths> as your entire response.\n");
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  BUILD CODE_WRITER EDIT PROMPT (single file)
    //  v5.2: явный блок TARGET_FILES из плана Guardian.
    // ══════════════════════════════════════════════════════════
    static string BuildCodeWriterEditPrompt(string filePath, string task, string fragment, string rangeStr,
        string guardianPlan, string acceptance)
    {
        string fileName = Path.GetFileName(filePath);
        var sb = new StringBuilder();
        sb.Append(CodeWriterSystemPrompt);
        sb.Append("\n");
        sb.Append("Task: " + task + "\n");
        sb.Append("File: " + fileName + "\n");
        if (rangeStr != null)
            sb.Append("Line range: " + rangeStr + "\n");

        // v5.1 FIX: план Guardian больше не теряется — передаётся явно.
        if (!string.IsNullOrWhiteSpace(guardianPlan))
        {
            sb.Append("\nPlan from SYSTEM_GUARDIAN (authoritative):\n");
            sb.Append(guardianPlan);
            sb.Append("\n");
        }

        // v5.2: явный блок TARGET_FILES
        string targetFiles = ExtractTargetFilesFromPlan(guardianPlan);
        string tfBlock = FormatTargetFilesBlock(targetFiles);
        if (tfBlock != null)
            sb.Append(tfBlock);

        if (!string.IsNullOrWhiteSpace(acceptance))
        {
            sb.Append("\nAcceptance criteria (must hold after edit):\n");
            sb.Append(acceptance);
            sb.Append("\n");
        }

        sb.Append("\nCurrent code:\n");
        sb.Append(fragment);
        sb.Append("\nReturn the modified file in FILE/ACTION/CONTENT/END_FILE format.");
        sb.Append("\nACTION should be MODIFY.");
        sb.Append("\nReturn the FULL file content, not just the changed lines.");
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  BUILD CODE_WRITER PROMPT (folder / plan-based)
    //  v5.2: явный блок TARGET_FILES из плана Guardian.
    // ══════════════════════════════════════════════════════════
    static string BuildCodeWriterPrompt(string task, string guardianPlan, string codePayload)
    {
        var sb = new StringBuilder();
        sb.Append(CodeWriterSystemPrompt);
        sb.Append("\n");
        sb.Append("Task: " + task + "\n");
        sb.Append("Plan from SYSTEM_GUARDIAN (authoritative):\n");
        sb.Append(guardianPlan);
        sb.Append("\n");

        // v5.2: явный блок TARGET_FILES
        string targetFiles = ExtractTargetFilesFromPlan(guardianPlan);
        string tfBlock = FormatTargetFilesBlock(targetFiles);
        if (tfBlock != null)
            sb.Append(tfBlock);

        if (!string.IsNullOrEmpty(codePayload))
        {
            sb.Append("\nCurrent source files (ground truth):\n");
            sb.Append(codePayload);
            sb.Append("\n");
        }
        sb.Append("\nGenerate the code. Return FILE/ACTION/CONTENT/END_FILE blocks.");
        sb.Append("\nEach block MUST end with END_FILE.");
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  BUILD CODE_WRITER FIX PROMPT (retry after Guardian FAIL)
    //  v5.2: передаёт TARGET_FILES чтобы retry не терял список файлов.
    // ══════════════════════════════════════════════════════════
    static string BuildCodeWriterFixPrompt(string task, string previousResponse, string errors, string coordinates)
    {
        return BuildCodeWriterFixPrompt(task, previousResponse, errors, coordinates, null, null, null);
    }

    static string BuildCodeWriterFixPrompt(string task, string previousResponse, string errors, string coordinates,
        string actualFileContent, string acceptance)
    {
        return BuildCodeWriterFixPrompt(task, previousResponse, errors, coordinates, actualFileContent, acceptance, null);
    }

    static string BuildCodeWriterFixPrompt(string task, string previousResponse, string errors, string coordinates,
        string actualFileContent, string acceptance, string targetFiles)
    {
        var sb = new StringBuilder();
        sb.Append(CodeWriterSystemPrompt);
        sb.Append("\n");
        sb.Append("Your previous response FAILED validation.\n");
        sb.Append("Original task: " + task + "\n");

        // v5.2: TARGET_FILES передаётся явно чтобы retry не терял список
        string tfBlock = FormatTargetFilesBlock(targetFiles);
        if (tfBlock != null)
            sb.Append(tfBlock);

        if (!string.IsNullOrEmpty(previousResponse))
        {
            sb.Append("Your previous output:\n");
            string prev = previousResponse;
            if (prev.Length > 8000) prev = prev.Substring(0, 8000) + "\n... [truncated]";
            sb.Append(prev);
            sb.Append("\n");
        }
        sb.Append("ERRORS from SYSTEM_GUARDIAN:\n");
        sb.Append(errors ?? "unknown");
        sb.Append("\n");
        if (!string.IsNullOrEmpty(coordinates))
        {
            sb.Append("COORDINATES: " + coordinates + "\n");
        }
        // v5.1: дельта — что ожидалось vs что получилось на диске.
        if (!string.IsNullOrEmpty(acceptance))
        {
            sb.Append("\nExpected (acceptance criteria):\n");
            sb.Append(acceptance);
            sb.Append("\n");
        }
        if (!string.IsNullOrEmpty(actualFileContent))
        {
            sb.Append("\nActual file content on disk now:\n");
            string act = actualFileContent;
            if (act.Length > 8000) act = act.Substring(0, 8000) + "\n... [truncated]";
            sb.Append(act);
            sb.Append("\n");
        }
        sb.Append("\nFix the errors and return corrected FILE/ACTION/CONTENT/END_FILE blocks.");
        sb.Append("\nEach block MUST end with END_FILE.");
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  PARSE CODE_WRITER RESPONSE (v5.0 — исправленный)
    // ══════════════════════════════════════════════════════════
    static CodeWriterResult ParseCodeWriterResponse(string raw)
    {
        var result = new CodeWriterResult();
        result.RawText = raw ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            result.ValidationErrors.Add("Empty response from CodeWriter");
            return result;
        }

        WriteColored(ConsoleColor.DarkGray,
            "    [CW-parse] Начало: " + raw.Length + " символов\n");

        string cleaned = StripCodeWriterFences(raw);
        string[] lines = cleaned.Split(new[] { "\n" }, StringSplitOptions.None);
        string currentPath = null;
        string currentAction = null;
        var contentBuilder = new StringBuilder();
        bool inContent = false;
        bool hasAnyBlock = false;
        int blockCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.Trim();

            if (trimmed.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
            {
                if (currentPath != null)
                {
                    SaveParsedBlock(result, currentPath, currentAction,
                        contentBuilder.ToString(), inContent);
                    blockCount++;
                }
                currentPath = trimmed.Substring(5).Trim().Trim('"');
                currentAction = null;
                contentBuilder = new StringBuilder();
                inContent = false;
                hasAnyBlock = true;
                continue;
            }

            if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase)
                && currentPath != null && !inContent)
            {
                currentAction = trimmed.Substring(7).Trim().ToUpperInvariant();
                if (currentAction != "CREATE" && currentAction != "MODIFY" && currentAction != "DELETE")
                    currentAction = "MODIFY";
                continue;
            }

            if (trimmed.StartsWith("CONTENT:", StringComparison.OrdinalIgnoreCase)
                && currentPath != null && !inContent)
            {
                inContent = true;
                string afterColon = trimmed.Substring(8);
                if (afterColon.Length > 0)
                {
                    contentBuilder.Append(afterColon);
                    contentBuilder.Append("\n");
                }
                continue;
            }

            if (trimmed == "END_FILE" && currentPath != null)
            {
                SaveParsedBlock(result, currentPath, currentAction,
                    contentBuilder.ToString(), true);
                blockCount++;
                currentPath = null;
                currentAction = null;
                contentBuilder = new StringBuilder();
                inContent = false;
                continue;
            }

            if (inContent && currentPath != null)
            {
                contentBuilder.Append(line);
                contentBuilder.Append("\n");
            }
        }

        if (currentPath != null)
        {
            if (inContent)
            {
                result.ValidationErrors.Add(
                    "Block '" + currentPath + "' not closed with END_FILE (response truncated?)");
            }
            SaveParsedBlock(result, currentPath, currentAction,
                contentBuilder.ToString(), inContent);
            blockCount++;
        }

        result.HasValidMarkers = hasAnyBlock && result.Operations.Count > 0;
        if (hasAnyBlock && result.Operations.Count == 0)
        {
            result.ValidationErrors.Add("FILE marker found but no complete block (missing END_FILE?)");
        }
        if (!hasAnyBlock)
        {
            result.ValidationErrors.Add("No FILE/ACTION/CONTENT/END_FILE markers found");
        }
        result.PlanConfirmed = result.Operations.Count > 0;

        WriteColored(ConsoleColor.DarkGray,
            "    [CW-parse] Найдено блоков: " + blockCount +
            ", операций: " + result.Operations.Count +
            ", ошибок: " + result.ValidationErrors.Count + "\n");
        foreach (var op in result.Operations)
        {
            int contentLen = op.Content != null ? op.Content.Length : 0;
            WriteColored(ConsoleColor.DarkGray,
                "    [CW-parse]   " + (op.Action ?? "?") + " " +
                (op.Path ?? "?") + " (" + contentLen + " chars)\n");
        }
        return result;
    }

    static void SaveParsedBlock(CodeWriterResult result, string path, string action,
        string content, bool hadContent)
    {
        var op = new FileOperation();
        op.Path = path;
        op.Action = action ?? "MODIFY";

        if (op.IsDelete)
        {
            op.Content = "";
        }
        else
        {
            op.Content = content;
            if (op.Content.EndsWith("\n"))
                op.Content = op.Content.Substring(0, op.Content.Length - 1);
            if (op.Content.EndsWith("\r"))
                op.Content = op.Content.Substring(0, op.Content.Length - 1);
        }
        result.Operations.Add(op);
        if (!result.FilesAffected.Contains(path))
            result.FilesAffected.Add(path);
    }

    // ══════════════════════════════════════════════════════════
    //  STRIP CODE_WRITER FENCES (markdown-обёртки)
    // ══════════════════════════════════════════════════════════
    static string StripCodeWriterFences(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string t = text.Trim();
        if (t.StartsWith("```"))
        {
            int firstNl = t.IndexOf('\n');
            if (firstNl >= 0)
                t = t.Substring(firstNl + 1);
            else
                t = t.Substring(3);
        }
        if (t.EndsWith("```"))
            t = t.Substring(0, t.Length - 3);
        return t.TrimEnd('\r', '\n');
    }

    // ══════════════════════════════════════════════════════════
    //  GET FILE CONTENT FROM RESULT
    // ══════════════════════════════════════════════════════════
    static string GetFileContentFromResult(CodeWriterResult result, string fileName)
    {
        if (result == null || result.IsEmpty || string.IsNullOrEmpty(fileName))
            return null;

        foreach (var op in result.Operations)
        {
            if (op.Path == null) continue;
            string opName = Path.GetFileName(op.Path.Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(opName, fileName, StringComparison.OrdinalIgnoreCase))
                return op.Content;
        }

        foreach (var op in result.Operations)
        {
            if (op.Path != null && string.Equals(op.Path, fileName, StringComparison.OrdinalIgnoreCase))
                return op.Content;
        }

        if (result.Operations.Count == 1)
            return result.Operations[0].Content;

        return null;
    }
}
