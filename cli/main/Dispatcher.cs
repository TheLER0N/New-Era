// Dispatcher.cs — AI #2 как диспетчер: улучшение промпта, выбор файлов,
// извлечение кода, сжатие контекста, валидация.
// New Era CLI v6.0
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  КОНФИГ
    // ══════════════════════════════════════════════════════════
    static bool DispatcherEnabled = false;
    static bool CompressEnabled   = false;
    static bool ExtractEnabled    = false;

    const int DispatchTimeoutMs          = 90000;
    const int DispatchReadWriteTimeoutMs = 120000;

    // ══════════════════════════════════════════════════════════
    //  ПРОМПТЫ
    // ══════════════════════════════════════════════════════════
    const string DispatchPromptEnhance =
        "You are a prompt optimizer. Rewrite the user's task to be maximally clear " +
        "for a code-generation LLM. Keep original intent. Add specificity. " +
        "Output ONLY the rewritten task. No preamble, no markdown.";

    const string DispatchPromptSelectFiles =
        "You are a file selector. Given a task and project tree, return ONLY JSON: " +
        "{\"files\": [\"relative/path1\", ...], \"actions\": {\"path\": \"READ|MODIFY|CREATE|DELETE\"}}. " +
        "3-15 files max. No markdown, no explanation.";

    const string DispatchPromptCompress =
        "You are a context compressor. Summarize the chat history into a short briefing " +
        "(max 500 words). Keep: key decisions, file names, errors, current task. " +
        "Drop: code blocks, verbose explanations, greetings. Output ONLY the summary.";

    const string DispatchPromptExtract =
        "You are a code extractor. From the AI response below, extract ALL file operations. " +
        "Return ONLY blocks in this exact format:\n" +
        "FILE: relative/path\n" +
        "ACTION: CREATE|MODIFY|DELETE\n" +
        "CONTENT:\n" +
        "...code...\n" +
        "END_FILE\n" +
        "No explanations, no markdown fences. If no code found, return: NO_CODE";

    // ══════════════════════════════════════════════════════════
    //  ENTRY POINT
    // ══════════════════════════════════════════════════════════
    static DispatchResult DispatchRequest(string userInput, string projectPath)
    {
        var result = new DispatchResult();
        result.OriginalInput = userInput;
        result.EnhancedPrompt = userInput;

        if (!DispatcherEnabled || string.IsNullOrWhiteSpace(userInput))
            return result;

        if (!IsAi2Configured())
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: AI #2 не сконфигурирован — bypass\n");
            return result;
        }

        if (CompressEnabled)
        {
            try
            {
                string summary = CompressChatContext();
                if (!string.IsNullOrWhiteSpace(summary))
                    result.ContextSummary = summary;
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Yellow,
                    "  ⚠ dispatcher: сжатие контекста (" + ex.Message + ")\n");
            }
        }

        try
        {
            string enhanced = EnhancePromptViaAI2(userInput);

            if (!string.IsNullOrWhiteSpace(enhanced) && enhanced.Length >= userInput.Length / 3)
            {
                result.EnhancedPrompt = enhanced;
                WriteColored(ConsoleColor.DarkGray, "  ◌ dispatcher: промпт улучшен\n");
            }
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: улучшение (" + ex.Message + ")\n");
        }

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            try
            {
                var selection = SelectFilesViaAI2(result.EnhancedPrompt, projectPath);

                if (selection != null && selection.Count > 0)
                {
                    result.SelectedFiles = selection;
                    WriteColored(ConsoleColor.DarkGray,
                        "  ◌ dispatcher: файлов выбрано: " + selection.Count + "\n");
                }
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Yellow,
                    "  ⚠ dispatcher: выбор файлов (" + ex.Message + ")\n");
            }
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  ENHANCE
    // ══════════════════════════════════════════════════════════
    static string EnhancePromptViaAI2(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
            return null;

        string response = PostDispatchMessage(DispatchPromptEnhance, task);

        if (string.IsNullOrWhiteSpace(response))
            return null;

        return StripMarkdownFences(response).Trim();
    }

    // ══════════════════════════════════════════════════════════
    //  SELECT FILES
    // ══════════════════════════════════════════════════════════
    static List<FileSelection> SelectFilesViaAI2(string task, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(projectPath))
            return null;

        string structure = ScanDirectory(projectPath, 0);

        if (structure.Length > 8000)
            structure = structure.Substring(0, 8000) + "\n... [truncated]";

        string userPrompt =
            "Task: " + task + "\n" +
            "Project structure:\n" + structure;

        string response = PostDispatchMessage(DispatchPromptSelectFiles, userPrompt);

        if (string.IsNullOrWhiteSpace(response))
            return null;

        return ParseFileSelection(response);
    }

    static List<FileSelection> ParseFileSelection(string response)
    {
        var result = new List<FileSelection>();
        string cleaned = StripMarkdownFences(response);

        try
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;

            var obj = ser.DeserializeObject(cleaned) as Dictionary<string, object>;
            if (obj == null)
                return result;

            if (obj.ContainsKey("files"))
            {
                object[] arr = obj["files"] as object[];

                if (arr != null)
                {
                    foreach (object item in arr)
                    {
                        string path = item as string;

                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            var fs = new FileSelection();
                            fs.Path = path.Replace('\\', '/');
                            fs.Action = "READ";
                            result.Add(fs);

                            if (result.Count >= 15)
                                break;
                        }
                    }
                }
            }

            if (obj.ContainsKey("actions"))
            {
                var actions = obj["actions"] as Dictionary<string, object>;

                if (actions != null)
                {
                    foreach (var kv in actions)
                    {
                        string action = kv.Value as string;
                        if (string.IsNullOrEmpty(action))
                            continue;

                        action = action.ToUpperInvariant();

                        bool found = false;

                        foreach (var fs in result)
                        {
                            if (fs.Path == kv.Key.Replace('\\', '/'))
                            {
                                fs.Action = action;
                                found = true;
                                break;
                            }
                        }

                        if (!found && result.Count < 15)
                        {
                            var fs = new FileSelection();
                            fs.Path = kv.Key.Replace('\\', '/');
                            fs.Action = action;
                            result.Add(fs);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  COMPRESS
    // ══════════════════════════════════════════════════════════
    static string CompressChatContext()
    {
        string snapshot;

        lock (HistoryLock)
        {
            if (History.Count < 4)
                return null;

            var sb = new StringBuilder();
            int start = Math.Max(0, History.Count - 20);

            for (int i = start; i < History.Count; i++)
            {
                var e = History[i];
                string preview = (e.Text ?? "").Replace("\n", " ");

                if (preview.Length > 200)
                    preview = preview.Substring(0, 200) + "...";

                sb.Append("[" + (e.Role ?? "?") + "] " + preview + "\n");
            }

            snapshot = sb.ToString();
        }

        if (snapshot.Length < 100)
            return null;

        string response = PostDispatchMessage(DispatchPromptCompress, snapshot);

        if (string.IsNullOrWhiteSpace(response))
            return null;

        return StripMarkdownFences(response).Trim();
    }

    // ══════════════════════════════════════════════════════════
    //  EXTRACT CODE
    // ══════════════════════════════════════════════════════════
    static CodeWriterResult ExtractCodeViaAI2(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        string truncated = rawResponse;

        if (truncated.Length > 12000)
            truncated = truncated.Substring(0, 12000) + "\n... [truncated]";

        string response = PostDispatchMessage(DispatchPromptExtract, truncated);

        if (string.IsNullOrWhiteSpace(response))
            return null;

        if (response.Trim().ToUpperInvariant().Contains("NO_CODE"))
            return null;

        return ParseCodeWriterResponse(response);
    }

    // ══════════════════════════════════════════════════════════
    //  HTTP AI #2
    // ══════════════════════════════════════════════════════════
    static string PostDispatchMessage(string systemPrompt, string userPrompt)
    {
        if (!IsAi2Configured())
            throw new Exception("dispatcher: AI #2 не сконфигурирован (AI2_TOKEN + AI2_CHAT_ID)");

        string api   = GetAi2Api();
        string model = GetAi2Model();
        string token = GetAi2Token();
        string chat  = ChatId2;

        return PostRoleChatMessage(
            "Dispatcher",
            systemPrompt,
            userPrompt,
            model,
            api,
            token,
            chat,
            DispatchTimeoutMs,
            DispatchReadWriteTimeoutMs
        );
    }

    // ══════════════════════════════════════════════════════════
    //  LOCAL FALLBACK EXTRACTOR
    // ══════════════════════════════════════════════════════════
    static CodeWriterResult ExtractCodeOrLocal(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        if (DispatcherEnabled && ExtractEnabled && IsAi2Configured())
        {
            try
            {
                CodeWriterResult r = ExtractCodeViaAI2(rawResponse);
                if (r != null && !r.IsEmpty)
                    return r;
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Yellow,
                    "  ⚠ extractor: " + ex.Message + " — локальный fallback\n");
            }
        }

        if (LooksLikeCodeWriterMarkers(rawResponse))
            return ParseCodeWriterResponse(rawResponse);

        if (LooksLikeLegacyFileBlocks(rawResponse))
            return ConvertLegacyFileBlocks(ParseFileBlocks(rawResponse));

        return null;
    }

    // ══════════════════════════════════════════════════════════
    //  OPTIONAL AI #2 VALIDATION
    // ══════════════════════════════════════════════════════════
    static bool ValidateOperationsViaAI2(CodeWriterResult result, out string details)
    {
        details = null;

        if (!Ai2ValidateEnabled)
            return true;

        if (result == null || result.IsEmpty)
            return true;

        if (!IsAi2Configured())
            return true;

        var sb = new StringBuilder();

        sb.Append("Validate these proposed file operations.\n");
        sb.Append("Check syntax, completeness, obvious logic errors.\n");
        sb.Append("Respond PASS or FAIL with short reasons.\n");

        foreach (var op in result.Operations)
        {
            string content = op.Content ?? "";

            if (content.Length > 3000)
                content = content.Substring(0, 3000) + "\n... [truncated]";

            sb.Append("\nFILE: " + (op.Path ?? "unknown") + "\n");
            sb.Append("ACTION: " + (op.Action ?? "MODIFY") + "\n");
            sb.Append("CONTENT:\n");
            sb.Append(content);
            sb.Append("\nEND_FILE\n");
        }

        try
        {
            string response = PostDispatchMessage(
                "You are a code validator. Output PASS or FAIL and short errors.",
                sb.ToString()
            );

            details = response;
            return IsAi2Pass(response);
        }
        catch (Exception ex)
        {
            details = "validator unavailable: " + ex.Message;
            return true;
        }
    }

    static bool IsAi2Pass(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string upper = raw.ToUpperInvariant();

        if (upper.Contains("FAIL"))
            return false;

        return upper.Contains("PASS");
    }

    // ══════════════════════════════════════════════════════════
    //  NORMALIZE SINGLE FILE OPERATION
    // ══════════════════════════════════════════════════════════
    static void NormalizeSingleFileOperation(CodeWriterResult result, string filePath, string projectPath)
    {
        if (result == null || result.IsEmpty || string.IsNullOrEmpty(filePath))
            return;

        string fileName = Path.GetFileName(filePath);
        string rel = MakeRelativePath(projectPath, filePath).Replace('\\', '/');

        foreach (var op in result.Operations)
        {
            if (string.IsNullOrWhiteSpace(op.Path))
                continue;

            string opName = Path.GetFileName(op.Path.Replace('/', Path.DirectorySeparatorChar));

            if (string.Equals(opName, fileName, StringComparison.OrdinalIgnoreCase))
                op.Path = rel;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PRIMARY PROMPT
    // ══════════════════════════════════════════════════════════
    static string BuildPrimaryPrompt(DispatchResult dispatch, string projectPath)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(dispatch.ContextSummary))
        {
            sb.Append("CONTEXT SUMMARY (from previous conversation):\n");
            sb.Append(dispatch.ContextSummary);
            sb.Append("\n");
        }

        sb.Append(dispatch.EnhancedPrompt);

        if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0
            && !string.IsNullOrWhiteSpace(projectPath))
        {
            string payload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, projectPath);

            if (!string.IsNullOrEmpty(payload))
            {
                sb.Append("\nCurrent source files:\n");
                sb.Append(payload);
            }
        }

        return sb.ToString();
    }

    static string BuildSelectivePayloadFromSelection(List<FileSelection> selection, string projectPath)
    {
        var paths = new List<string>();

        foreach (var fs in selection)
        {
            if (fs.Action != "DELETE")
                paths.Add(fs.Path);
        }

        return BuildSelectivePayload(paths, projectPath);
    }

    // ══════════════════════════════════════════════════════════
    //  SELECTIVE PAYLOAD
    // ══════════════════════════════════════════════════════════
    static string BuildSelectivePayload(List<string> fileList, string projectPath)
    {
        if (fileList == null || fileList.Count == 0 || string.IsNullOrWhiteSpace(projectPath))
            return null;

        string baseDir = null;

        try
        {
            baseDir = Directory.Exists(projectPath)
                ? projectPath
                : Path.GetDirectoryName(projectPath);
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(baseDir))
            baseDir = BaseDir;

        const int maxTotalChars = 120000;
        const int maxFileChars = 40000;

        var sb = new StringBuilder();
        long totalChars = 0;
        int included = 0;
        int skipped = 0;

        foreach (string relPath in fileList)
        {
            if (string.IsNullOrWhiteSpace(relPath))
            {
                skipped++;
                continue;
            }

            string fullPath;

            try
            {
                string normalized = relPath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .TrimStart(Path.DirectorySeparatorChar);

                fullPath = Path.IsPathRooted(normalized)
                    ? normalized
                    : Path.Combine(baseDir, normalized);

                fullPath = Path.GetFullPath(fullPath);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                skipped++;
                continue;
            }

            string body;

            try
            {
                body = ReadTextAuto(fullPath);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrEmpty(body))
            {
                skipped++;
                continue;
            }

            body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n');

            bool truncated = false;

            if (body.Length > maxFileChars)
            {
                body = body.Substring(0, maxFileChars);
                truncated = true;
            }

            long blockLen = (long)body.Length + relPath.Length + 40;

            if (totalChars + blockLen > maxTotalChars)
            {
                skipped++;
                continue;
            }

            totalChars += blockLen;
            included++;

            string displayPath = relPath.Replace('\\', '/');

            sb.Append("\n=== FILE: " + displayPath + " ===\n");
            sb.Append(body);
            sb.Append("\n");

            if (truncated)
                sb.Append("// [truncated to 40000 chars]\n");

            sb.Append("=== END ===\n");
        }

        if (included == 0)
            return null;

        if (skipped > 0)
            sb.Append("\n// [selective context: " + included + " loaded, " + skipped + " skipped]\n");

        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  MARKDOWN FENCES
    // ══════════════════════════════════════════════════════════
    static string StripMarkdownFences(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

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

        return t.Trim();
    }
}

// ══════════════════════════════════════════════════════════
//  CLASSES
// ══════════════════════════════════════════════════════════
class DispatchResult
{
    public string OriginalInput;
    public string EnhancedPrompt;
    public string ContextSummary;
    public List<FileSelection> SelectedFiles;
}

class FileSelection
{
    public string Path;
    public string Action;
}