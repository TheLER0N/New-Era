// Dispatcher.cs — AI #2 как диспетчер: улучшение промпта, выбор файлов,
// извлечение кода, сжатие контекста.
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  КОНФИГ (состояния)
    // ══════════════════════════════════════════════════════════
    static bool DispatcherEnabled = false;   // AI2_DISPATCHER=
    static bool CompressEnabled   = false;   // AI2_COMPRESS=
    static bool ExtractEnabled    = false;   // AI2_EXTRACT=

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
        "Return ONLY blocks in this exact format:" + "\n" +
        "FILE: relative/path" + "\n" +
        "ACTION: CREATE|MODIFY|DELETE" + "\n" +
        "CONTENT:" + "\n" +
        "...code..." + "\n" +
        "END_FILE" + "\n" +
        "No explanations, no markdown fences. If no code found, return: NO_CODE";

    // ══════════════════════════════════════════════════════════
    //  ENTRY POINT: полный цикл диспетчера
    // ══════════════════════════════════════════════════════════
    static DispatchResult DispatchRequest(string userInput, string projectPath)
    {
        var result = new DispatchResult();
        result.OriginalInput = userInput;
        result.EnhancedPrompt = userInput;

        if (!DispatcherEnabled || string.IsNullOrWhiteSpace(userInput))
            return result;

        // 1. Сжатие контекста
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
                    "  ⚠ dispatcher: сжатие контекста (" + ex.Message + ")" + "\n");
            }
        }

        // 2. Улучшение промпта
        try
        {
            string enhanced = EnhancePromptViaAI2(userInput);
            if (!string.IsNullOrWhiteSpace(enhanced) && enhanced.Length >= userInput.Length / 3)
            {
                result.EnhancedPrompt = enhanced;
                WriteColored(ConsoleColor.DarkGray, "  ◌ dispatcher: промпт улучшен" + "\n");
            }
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ dispatcher: улучшение (" + ex.Message + ")" + "\n");
        }

        // 3. Выбор файлов
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
                    "  ⚠ dispatcher: выбор файлов (" + ex.Message + ")" + "\n");
            }
        }

        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  УЛУЧШЕНИЕ ПРОМПТА
    // ══════════════════════════════════════════════════════════
    static string EnhancePromptViaAI2(string task)
    {
        if (string.IsNullOrWhiteSpace(task)) return null;
        string response = PostDispatchMessage(DispatchPromptEnhance, task);
        if (string.IsNullOrWhiteSpace(response)) return null;
        return StripMarkdownFences(response).Trim();
    }

    // ══════════════════════════════════════════════════════════
    //  ВЫБОР ФАЙЛОВ (до отправки Primary)
    // ══════════════════════════════════════════════════════════
    static List<FileSelection> SelectFilesViaAI2(string task, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(projectPath))
            return null;

        string structure = ScanDirectory(projectPath, 0);
        if (structure.Length > 8000)
            structure = structure.Substring(0, 8000) + "\n... [truncated]";

        string userPrompt = "Task: " + task + "\nProject structure:\n" + structure;
        string response = PostDispatchMessage(DispatchPromptSelectFiles, userPrompt);
        if (string.IsNullOrWhiteSpace(response)) return null;

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
            if (obj == null) return result;

            // files: ["path1", "path2"]
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
                            if (result.Count >= 15) break;
                        }
                    }
                }
            }

            // actions: {"path": "MODIFY", ...}
            if (obj.ContainsKey("actions"))
            {
                var actions = obj["actions"] as Dictionary<string, object>;
                if (actions != null)
                {
                    foreach (var kv in actions)
                    {
                        string action = kv.Value as string;
                        if (string.IsNullOrEmpty(action)) continue;
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
        catch { }

        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  СЖАТИЕ КОНТЕКСТА
    // ══════════════════════════════════════════════════════════
    static string CompressChatContext()
    {
        lock (HistoryLock)
        {
            if (History.Count < 4) return null;

            var sb = new StringBuilder();
            int start = Math.Max(0, History.Count - 20);
            for (int i = start; i < History.Count; i++)
            {
                var e = History[i];
                string preview = (e.Text ?? "").Replace("\n", " ");
                if (preview.Length > 200) preview = preview.Substring(0, 200) + "...";
                sb.Append("[" + (e.Role ?? "?") + "] " + preview + "\n");
            }

            if (sb.Length < 100) return null;

            string response = PostDispatchMessage(DispatchPromptCompress, sb.ToString());
            if (string.IsNullOrWhiteSpace(response)) return null;
            return StripMarkdownFences(response).Trim();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ИЗВЛЕЧЕНИЕ КОДА (после ответа Primary)
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
    //  HTTP: отправка в AI #2 (диспетчер)
    // ══════════════════════════════════════════════════════════
    static string PostDispatchMessage(string systemPrompt, string userPrompt)
    {
        string api   = (string.IsNullOrEmpty(ApiBaseUrl2) || ApiBaseUrl2 == DefaultApiBase)
                       ? ApiBaseUrl : ApiBaseUrl2;
        string model = string.IsNullOrEmpty(Ai2Model) ? PrimaryModel : Ai2Model;
        string token = Token2;
        string chat  = string.IsNullOrEmpty(ChatId2) ? ChatId : ChatId2;

        if (string.IsNullOrEmpty(token))
            throw new Exception("dispatcher: нет токена AI #2 (AI2_TOKEN)");
        if (string.IsNullOrEmpty(chat))
            throw new Exception("dispatcher: нет chat_id AI #2");

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
    //  ПОСТРОЕНИЕ ПРОМПТА ДЛЯ PRIMARY (с учётом диспетчера)
    // ══════════════════════════════════════════════════════════
    static string BuildPrimaryPrompt(DispatchResult dispatch, string projectPath)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(dispatch.ContextSummary))
        {
            sb.Append("CONTEXT SUMMARY (from previous conversation):" + "\n");
            sb.Append(dispatch.ContextSummary);
            sb.Append("\n\n");
        }

        sb.Append(dispatch.EnhancedPrompt);

        if (dispatch.SelectedFiles != null && dispatch.SelectedFiles.Count > 0
            && !string.IsNullOrWhiteSpace(projectPath))
        {
            string payload = BuildSelectivePayloadFromSelection(dispatch.SelectedFiles, projectPath);
            if (!string.IsNullOrEmpty(payload))
            {
                sb.Append("\n\nCurrent source files:\n");
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
}

// ══════════════════════════════════════════════════════════
//  КЛАССЫ
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
    public string Action;  // READ, MODIFY, CREATE, DELETE
}