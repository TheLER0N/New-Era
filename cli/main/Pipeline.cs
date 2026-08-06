// Pipeline.cs — AI#2 диспетчер: enhance, select, compress, extract, validate
// New Era v7.2
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
partial class MainConsole
{
const string PromptEnhance =
"You are a prompt optimizer for code editing tasks. Rewrite the user's task to be maximally clear. " +
"Output ONLY the rewritten task. No preamble, no markdown.";
const string PromptSelectFiles =
    "You are a file selector. Given a task and project tree, return ONLY JSON: " +
    "{\"files\": [\"relative/path1\"], \"actions\": {\"path\": \"READ|MODIFY|CREATE|DELETE\"}}. " +
    "3-15 files max. No markdown, no explanation.";

const string PromptCompress =
    "You are a context compressor. Summarize the chat history into a short briefing (max 500 words). " +
    "Keep: key decisions, file names, errors, current task. Output ONLY the summary.";

const string PromptExtract =
    "You are a code extractor. From the AI response below, extract ALL file operations. " +
    "Return ONLY blocks in this exact format:\n" +
    "FILE: relative/path\nACTION: CREATE|MODIFY|DELETE\nCONTENT:\n...code...\nEND_FILE\n" +
    "No explanations, no markdown fences. If no code found, return: NO_CODE";

// ══════════════════════════════════════════════
//  DISPATCH (enhance + compress + select)
// ══════════════════════════════════════════════

static DispatchResult DispatchRequest(string userInput, string projectPath)
{
    var result = new DispatchResult();
    result.OriginalInput = userInput;
    result.EnhancedPrompt = userInput;

    if (!DispatcherEnabled || string.IsNullOrWhiteSpace(userInput)) return result;

    if (!IsAi2Configured()) {
        WriteColored(ConsoleColor.Yellow, "  \u26A0 dispatcher: AI #2 не сконфигурирован — bypass\n");
        return result;
    }

    // 1. Compress
    if (CompressEnabled) {
        try {
            string summary = CompressChatContext();
            if (!string.IsNullOrWhiteSpace(summary)) result.ContextSummary = summary;
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 compress: " + ex.Message + "\n");
        }
        PauseBetweenRoles();
    }

    // 2. Enhance
    try {
        string enhanced = PostDispatchMessageWithRetry(PromptEnhance, userInput);
        if (!string.IsNullOrWhiteSpace(enhanced) && enhanced.Length >= userInput.Length / 3) {
            result.EnhancedPrompt = StripMarkdownFences(enhanced).Trim();
            WriteColored(ConsoleColor.DarkGray, "  \u25CC промпт улучшен\n");
        }
    } catch (Exception ex) {
        WriteColored(ConsoleColor.Yellow, "  \u26A0 enhance: " + ex.Message + " — bypass\n");
    }
    PauseBetweenRoles();

    // 3. Select
    if (!string.IsNullOrWhiteSpace(projectPath)) {
        try {
            var selection = SelectFilesViaAI2(result.EnhancedPrompt, projectPath);
            if (selection != null && selection.Count > 0) {
                result.SelectedFiles = selection;
                WriteColored(ConsoleColor.DarkGray, "  \u25CC файлов выбрано: " + selection.Count + "\n");
            }
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 select: " + ex.Message + "\n");
        }
    }

    return result;
}

// ══════════════════════════════════════════════
//  SELECT FILES
// ══════════════════════════════════════════════

static List<FileSelection> SelectFilesViaAI2(string task, string projectPath)
{
    string structure = ScanDirectory(projectPath, 0);
    if (structure.Length > 8000) structure = structure.Substring(0, 8000) + "\n... [truncated]";

    string contentPreview = BuildSelectPreview(projectPath, MaxContextTotal, MaxContextFile);

    var ub = new StringBuilder();
    ub.Append("Task: " + task + "\n");
    ub.Append("Project structure:\n" + structure + "\n");
    if (!string.IsNullOrEmpty(contentPreview))
        ub.Append("File contents preview:\n" + contentPreview + "\n");

    string response = PostDispatchMessageWithRetry(PromptSelectFiles, ub.ToString());
    if (string.IsNullOrWhiteSpace(response)) return null;

    return ParseFileSelection(response);
}

static string BuildSelectPreview(string projectPath, int maxTotal, int maxPerFile)
{
    try {
        if (!Directory.Exists(projectPath)) return null;

        var files = new List<string>();
        CollectContextFiles(projectPath, files);
        if (files.Count == 0) return null;

        files.Sort(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        long total = 0;

        foreach (string full in files) {
            string rel = MakeRelativePath(projectPath, full);
            string name = Path.GetFileName(full);
            if (IsExcludedContextFile(rel, name)) continue;

            string body;
            try { body = ReadTextAuto(full); } catch { continue; }
            if (string.IsNullOrEmpty(body)) continue;

            body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n');
            if (body.Length > maxPerFile) body = body.Substring(0, maxPerFile) + "\n... [truncated]";

            long blockLen = body.Length + rel.Length + 40;
            if (total + blockLen > maxTotal) continue;
            total += blockLen;

            sb.Append("=== " + rel + " ===\n");
            sb.Append(body);
            sb.Append("\n=== END ===\n");
        }
        return sb.Length > 0 ? sb.ToString() : null;
    } catch {
        return null;
    }
}

static List<FileSelection> ParseFileSelection(string response)
{
    var result = new List<FileSelection>();
    string cleaned = StripMarkdownFences(response);

    try {
        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var obj = ser.DeserializeObject(cleaned) as Dictionary<string, object>;
        if (obj == null) return result;

        if (obj.ContainsKey("files")) {
            object[] arr = obj["files"] as object[];
            if (arr != null)
                foreach (object item in arr) {
                    string path = item as string;
                    if (!string.IsNullOrWhiteSpace(path) && result.Count < 15)
                        result.Add(new FileSelection { Path = path.Replace('\\', '/'), Action = "READ" });
                }
        }

        if (obj.ContainsKey("actions")) {
            var actions = obj["actions"] as Dictionary<string, object>;
            if (actions != null)
                foreach (var kv in actions) {
                    string action = (kv.Value as string ?? "").ToUpperInvariant();
                    if (string.IsNullOrEmpty(action)) continue;

                    bool found = false;
                    foreach (var fs in result)
                        if (fs.Path == kv.Key.Replace('\\', '/')) { fs.Action = action; found = true; break; }

                    if (!found && result.Count < 15)
                        result.Add(new FileSelection { Path = kv.Key.Replace('\\', '/'), Action = action });
                }
        }
    } catch { }

    return result;
}

// ══════════════════════════════════════════════
//  COMPRESS
// ══════════════════════════════════════════════

static string CompressChatContext()
{
    string snapshot;
    lock (HistoryLock) {
        if (History.Count < 4) return null;

        var sb = new StringBuilder();
        int start = Math.Max(0, History.Count - 20);
        for (int i = start; i < History.Count; i++) {
            var e = History[i];
            string preview = (e.Text ?? "").Replace("\n", " ");
            if (preview.Length > 200) preview = preview.Substring(0, 200) + "...";
            sb.Append("[" + (e.Role ?? "?") + "] " + preview + "\n");
        }
        snapshot = sb.ToString();
    }

    if (snapshot.Length < 100) return null;

    string response = PostDispatchMessageWithRetry(PromptCompress, snapshot);
    if (string.IsNullOrWhiteSpace(response)) return null;

    return StripMarkdownFences(response).Trim();
}

// ══════════════════════════════════════════════
//  EXTRACT CODE
// ══════════════════════════════════════════════

static CodeWriterResult ExtractCodeOrLocal(string rawResponse)
{
    if (string.IsNullOrWhiteSpace(rawResponse)) return null;

    if (DispatcherEnabled && ExtractEnabled && IsAi2Configured()) {
        try {
            string truncated = rawResponse.Length > 12000
                ? rawResponse.Substring(0, 12000) + "\n... [truncated]" : rawResponse;

            string response = PostDispatchMessageWithRetry(PromptExtract, truncated);
            if (!string.IsNullOrWhiteSpace(response) &&
                !response.Trim().ToUpperInvariant().Contains("NO_CODE")) {
                CodeWriterResult r = ParseCodeWriterResponse(response);
                if (r != null && !r.IsEmpty) return r;
            }
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Yellow,
                "  \u26A0 extractor: " + ex.Message + " — локальный fallback\n");
        }
    }

    if (LooksLikeCodeWriterMarkers(rawResponse))
        return ParseCodeWriterResponse(rawResponse);

    if (LooksLikeLegacyFileBlocks(rawResponse))
        return ConvertLegacyFileBlocks(ParseFileBlocks(rawResponse));

    return null;
}

// ══════════════════════════════════════════════
//  VALIDATE
// ══════════════════════════════════════════════

static bool ValidateOperationsViaAI2(CodeWriterResult result, out string details)
{
    details = null;
    if (!Ai2ValidateEnabled || result == null || result.IsEmpty || !IsAi2Configured())
        return true;

    var sb = new StringBuilder();
    sb.Append("Validate these file operations. Check syntax, completeness. Respond PASS or FAIL.\n");

    foreach (var op in result.Operations) {
        string content = op.Content ?? "";
        if (content.Length > 3000) content = content.Substring(0, 3000) + "\n... [truncated]";
        sb.Append("\nFILE: " + (op.Path ?? "?") + "\nACTION: " + (op.Action ?? "MODIFY") +
            "\nCONTENT:\n" + content + "\nEND_FILE\n");
    }

    try {
        string response = PostDispatchMessageWithRetry(
            "You are a code validator. Output PASS or FAIL and short errors.", sb.ToString());
        details = response;
        if (string.IsNullOrWhiteSpace(response)) return true;
        string upper = response.ToUpperInvariant();
        if (upper.Contains("FAIL")) return false;
        return upper.Contains("PASS");
    } catch (Exception ex) {
        details = "validator unavailable: " + ex.Message;
        return true;
    }
}

// ══════════════════════════════════════════════
//  SELECTIVE PAYLOAD BUILDER
// ══════════════════════════════════════════════

static string BuildSelectivePayload(List<FileSelection> selection, string projectPath)
{
    var paths = new List<string>();
    foreach (var fs in selection)
        if (fs.Action != "DELETE") paths.Add(fs.Path);
    return BuildSelectivePayloadByPaths(paths, projectPath);
}

static string BuildSelectivePayloadByPaths(List<string> fileList, string projectPath)
{
    if (fileList == null || fileList.Count == 0 || string.IsNullOrWhiteSpace(projectPath)) return null;

    string baseDir = null;
    try { baseDir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath); } catch { }
    if (string.IsNullOrEmpty(baseDir)) baseDir = BaseDir;

    int maxTotal = MaxContextTotal, maxFile = MaxContextFile;
    var sb = new StringBuilder();
    long total = 0; int included = 0, skipped = 0;

    foreach (string relPath in fileList) {
        if (string.IsNullOrWhiteSpace(relPath)) { skipped++; continue; }

        string fullPath;
        try {
            string norm = relPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            fullPath = Path.IsPathRooted(norm) ? norm : Path.Combine(baseDir, norm);
            fullPath = Path.GetFullPath(fullPath);
        } catch { skipped++; continue; }

        if (!File.Exists(fullPath)) { skipped++; continue; }

        string body;
        try { body = ReadTextAuto(fullPath); } catch { skipped++; continue; }
        if (string.IsNullOrEmpty(body)) { skipped++; continue; }

        body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n');
        bool truncated = false;
        if (body.Length > maxFile) { body = body.Substring(0, maxFile); truncated = true; }

        long blockLen = (long)body.Length + relPath.Length + 40;
        if (total + blockLen > maxTotal) { skipped++; continue; }
        total += blockLen; included++;

        sb.Append("\n=== FILE: " + relPath.Replace('\\', '/') + " ===\n");
        sb.Append(body);
        sb.Append("\n");
        if (truncated) sb.Append("// [truncated]\n");
        sb.Append("=== END ===\n");
    }

    if (included == 0) return null;
    return sb.ToString();
}

// ══════════════════════════════════════════════
//  HELPERS
// ══════════════════════════════════════════════

static string StripMarkdownFences(string text)
{
    if (string.IsNullOrEmpty(text)) return "";
    string t = text.Trim();
    if (t.StartsWith("```")) {
        int firstNl = t.IndexOf('\n');
        if (firstNl >= 0) t = t.Substring(firstNl + 1);
        else t = t.Substring(3);
    }
    if (t.EndsWith("```")) t = t.Substring(0, t.Length - 3);
    return t.Trim();
}

static bool LooksLikeCodeWriterMarkers(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return false;
    return text.IndexOf("FILE:", StringComparison.OrdinalIgnoreCase) >= 0 &&
           text.IndexOf("END_FILE", StringComparison.OrdinalIgnoreCase) >= 0;
}

static bool LooksLikeLegacyFileBlocks(string text)
{
    if (string.IsNullOrWhiteSpace(text)) return false;
    return text.IndexOf("=== FILE:", StringComparison.OrdinalIgnoreCase) >= 0 &&
           text.IndexOf("=== END ===", StringComparison.OrdinalIgnoreCase) >= 0;
}

static Dictionary<string, string> ParseFileBlocks(string text)
{
    var result = new Dictionary<string, string>();
    if (string.IsNullOrEmpty(text)) return result;
    string[] lines = text.Split(new[] { "\n" }, StringSplitOptions.None);
    string currentFile = null;
    var content = new StringBuilder();
    foreach (string rawLine in lines) {
        string line = rawLine.TrimEnd('\r');
        string trimmed = line.Trim();
        if (currentFile == null && trimmed.StartsWith("```")) continue;
        if (trimmed.StartsWith("=== FILE:")) {
            if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n');
            currentFile = trimmed.Substring(9).Trim();
            content = new StringBuilder();
        } else if (trimmed.StartsWith("=== END ===")) {
            if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n');
            currentFile = null;
        } else if (currentFile != null) {
            content.Append(line).Append("\n");
        }
    }
    if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n');
    return result;
}

static CodeWriterResult ConvertLegacyFileBlocks(Dictionary<string, string> blocks)
{
    var result = new CodeWriterResult();
    if (blocks == null || blocks.Count == 0) return result;
    foreach (var kv in blocks) {
        result.Operations.Add(new CodeOperation { Path = kv.Key, Action = "MODIFY", Content = kv.Value });
    }
    return result;
}
}
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