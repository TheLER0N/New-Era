// FileOps.cs — файловые операции: парсинг, применение, rollback, лог
// New Era v7.1
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

class FileOperation
{
    public string Path;
    public string Action;
    public string Content;

    public bool IsCreate { get { return Action != null && Action.ToUpperInvariant() == "CREATE"; } }
    public bool IsModify { get { return Action != null && Action.ToUpperInvariant() == "MODIFY"; } }
    public bool IsDelete { get { return Action != null && Action.ToUpperInvariant() == "DELETE"; } }
}

class CodeWriterResult
{
    public string RawText = "";
    public bool HasValidMarkers = false;
    public List<FileOperation> Operations = new List<FileOperation>();
    public List<string> FilesAffected = new List<string>();
    public List<string> ValidationErrors = new List<string>();
    public bool IsEmpty { get { return Operations.Count == 0; } }
}

class RollbackEntry
{
    public string FilePath;
    public string Content;
    public string Timestamp;
}

partial class MainConsole
{
    const int MaxRollbackEntries  = 50;
    const int MaxChangeLogEntries = 200;

    static readonly List<RollbackEntry> RollbackHistory = new List<RollbackEntry>();
    static readonly List<string> ChangeLog = new List<string>();

    // ══════════════════════════════════════════════
    //  PARSE FILE/ACTION/CONTENT/END_FILE (P1: улучшенный)
    // ══════════════════════════════════════════════
    static CodeWriterResult ParseCodeWriterResponse(string raw)
    {
        var result = new CodeWriterResult { RawText = raw ?? "" };
        if (string.IsNullOrWhiteSpace(raw)) return result;

        // P1: защита от NO_CODE / мусора AI#2
        string upperRaw = raw.Trim().ToUpperInvariant();
        if (upperRaw == "NO_CODE" || upperRaw.StartsWith("NO_CODE")) return result;

        string cleaned = StripMarkdownFences(raw);
        string[] lines = cleaned.Split(new[] { "\n" }, StringSplitOptions.None);

        string currentPath = null, currentAction = null;
        var contentBuilder = new StringBuilder();
        bool inContent = false, hasAnyBlock = false;

        foreach (string rawLine in lines) {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();

            if (trimmed.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase)) {
                if (currentPath != null)
                    SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString());

                currentPath = trimmed.Substring(5).Trim().Trim('"');
                currentAction = null;
                contentBuilder = new StringBuilder();
                inContent = false;
                hasAnyBlock = true;
                continue;
            }

            if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase) && currentPath != null && !inContent) {
                currentAction = trimmed.Substring(7).Trim().ToUpperInvariant();
                if (currentAction != "CREATE" && currentAction != "MODIFY" && currentAction != "DELETE")
                    currentAction = "MODIFY";
                continue;
            }

            if (trimmed.StartsWith("CONTENT:", StringComparison.OrdinalIgnoreCase) && currentPath != null && !inContent) {
                inContent = true;
                string afterColon = trimmed.Substring(8);
                if (afterColon.Length > 0) {
                    contentBuilder.Append(afterColon);
                    contentBuilder.Append("\n");
                }
                continue;
            }

            if (trimmed == "END_FILE" && currentPath != null) {
                SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString());
                currentPath = null; currentAction = null;
                contentBuilder = new StringBuilder();
                inContent = false;
                continue;
            }

            if (inContent && currentPath != null) {
                contentBuilder.Append(line);
                contentBuilder.Append("\n");
            }
        }

        if (currentPath != null)
            SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString());

        result.HasValidMarkers = hasAnyBlock && result.Operations.Count > 0;
        return result;
    }

    static void SaveParsedBlock(CodeWriterResult result, string path, string action, string content)
    {
        var op = new FileOperation { Path = path, Action = action ?? "MODIFY" };

        if (op.IsDelete) {
            op.Content = "";
        } else {
            op.Content = content;
            if (op.Content.EndsWith("\n")) op.Content = op.Content.Substring(0, op.Content.Length - 1);
            if (op.Content.EndsWith("\r")) op.Content = op.Content.Substring(0, op.Content.Length - 1);
        }

        result.Operations.Add(op);
        if (!result.FilesAffected.Contains(path)) result.FilesAffected.Add(path);
    }

    // ══════════════════════════════════════════════
    //  LEGACY === FILE: === / === END === PARSER
    // ══════════════════════════════════════════════
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

            if (trimmed.StartsWith("=== FILE:") && trimmed.EndsWith("===")) {
                if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n');
                currentFile = trimmed.Substring(9, trimmed.Length - 12).Trim();
                content = new StringBuilder();
            } else if (trimmed == "=== END ===" && currentFile != null) {
                result[currentFile] = content.ToString().TrimEnd('\r', '\n');
                currentFile = null; content = new StringBuilder();
            } else if (currentFile != null) {
                content.Append(line); content.Append("\n");
            }
        }

        if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n');
        return result;
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

    static CodeWriterResult ConvertLegacyFileBlocks(Dictionary<string, string> blocks)
    {
        var result = new CodeWriterResult();
        if (blocks == null || blocks.Count == 0) return result;

        foreach (var kv in blocks) {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            var op = new FileOperation { Path = kv.Key.Replace('\\', '/'), Action = "MODIFY", Content = kv.Value };
            result.Operations.Add(op);
            if (!result.FilesAffected.Contains(op.Path)) result.FilesAffected.Add(op.Path);
        }

        result.HasValidMarkers = result.Operations.Count > 0;
        return result;
    }

    // ══════════════════════════════════════════════
    //  APPLY FILES
    // ══════════════════════════════════════════════
    static bool ApplyGeneratedFiles(CodeWriterResult result, string baseDir, bool autoConfirm)
    {
        if (result == null || result.IsEmpty) return false;

        Console.WriteLine();
        foreach (var op in result.Operations)
            WriteColored(ConsoleColor.Cyan, "  \u25B8 " + (op.Action ?? "MODIFY") + " " + (op.Path ?? "?") + "\n");

        // DELETE-подтверждение
        var deleteTargets = new List<string>();
        foreach (var op in result.Operations) {
            if (!op.IsDelete || string.IsNullOrWhiteSpace(op.Path)) continue;
            string outPath;
            if (TryResolveSafeOutputPath(baseDir, op.Path, out outPath) && File.Exists(outPath))
                deleteTargets.Add(outPath);
        }

        bool deleteApproved = true;
        if (deleteTargets.Count > 0 && !ArcMode) {
            foreach (string delPath in deleteTargets) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  \u26A0 Удалить файл? " + delPath + " [y/N] ");
                Console.ResetColor();

                string delConfirm = Console.ReadLine();
                if (delConfirm == null || delConfirm.Trim().ToLowerInvariant() != "y") {
                    deleteApproved = false;
                    WriteColored(ConsoleColor.DarkGray, "  \u25C2 Удаление отменено: " + delPath + "\n");
                }
            }
        }

        bool doWrite;
        if (autoConfirm || ArcMode) {
            WriteColored(ConsoleColor.Green, "  \u2714 Авто-применение\n");
            doWrite = true;
        } else {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  \u2753 Применить файлы? [y/N] ");
            Console.ResetColor();

            string confirm = Console.ReadLine();
            doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y";
        }

        if (!doWrite) { WriteColored(ConsoleColor.DarkGray, "  \u25C2 Отменено.\n"); return false; }

        if (string.IsNullOrEmpty(baseDir)) baseDir = BaseDir;

        int written = 0;
        foreach (var op in result.Operations) {
            if (string.IsNullOrWhiteSpace(op.Path)) continue;

            string outPath;
            if (!TryResolveSafeOutputPath(baseDir, op.Path, out outPath)) {
                WriteColored(ConsoleColor.Red, "  \u2716 " + op.Path + ": путь вне проекта\n");
                LogChange(op.Path, op.Action ?? "MODIFY", "error");
                continue;
            }

            try {
                if (op.IsDelete) {
                    if (!File.Exists(outPath)) { LogChange(outPath, "DELETE", "not_found"); continue; }
                    if (!deleteApproved && !ArcMode) { LogChange(outPath, "DELETE", "cancelled"); continue; }

                    SaveRollbackSnapshot(outPath);
                    File.Delete(outPath);
                    WriteColored(ConsoleColor.Red, "  \u2716 DELETE " + outPath + "\n");
                    LogChange(outPath, "DELETE", "success");
                    written++;
                } else {
                    SaveRollbackSnapshot(outPath);

                    string dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string content = op.Content ?? "";
                    if (!content.EndsWith("\n")) content += "\n";

                    File.WriteAllText(outPath, content, new UTF8Encoding(false));
                    WriteColored(ConsoleColor.Green, "  \u2714 " + outPath + "\n");
                    LogChange(outPath, op.Action ?? "MODIFY", "success");
                    written++;
                }
            } catch (Exception ex) {
                WriteColored(ConsoleColor.Red, "  \u2716 " + outPath + ": " + ex.Message + "\n");
                LogChange(outPath, op.Action ?? "MODIFY", "error");
            }
        }

        WriteColored(ConsoleColor.Green, "\n\u2714 Записано файлов: " + written + "\n");
        return written > 0;
    }

    // ══════════════════════════════════════════════
    //  ROLLBACK + CHANGELOG
    // ══════════════════════════════════════════════
    static void SaveRollbackSnapshot(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try {
            string content = File.Exists(filePath) ? ReadTextAuto(filePath) : null;

            lock (RollbackHistory) {
                RollbackHistory.Add(new RollbackEntry {
                    FilePath = filePath,
                    Content = content,
                    Timestamp = DateTime.Now.ToString("dd.MM HH:mm:ss")
                });
                while (RollbackHistory.Count > MaxRollbackEntries) RollbackHistory.RemoveAt(0);
            }
        } catch { }
    }

    static void LogChange(string file, string action, string status)
    {
        string entry = "[" + DateTime.Now.ToString("dd.MM HH:mm:ss") + "] " +
                       "FILE: " + (file ?? "?") + " | ACTION: " + (action ?? "MODIFY") + " | STATUS: " + (status ?? "?");

        lock (ChangeLog) {
            ChangeLog.Add(entry);
            while (ChangeLog.Count > MaxChangeLogEntries) ChangeLog.RemoveAt(0);
        }
    }

    static void NormalizeSingleFileOperation(CodeWriterResult result, string filePath, string projectPath)
    {
        if (result == null || result.IsEmpty || string.IsNullOrEmpty(filePath)) return;

        string fileName = Path.GetFileName(filePath);
        string rel = MakeRelativePath(projectPath, filePath).Replace('\\', '/');

        foreach (var op in result.Operations) {
            if (string.IsNullOrWhiteSpace(op.Path)) continue;
            string opName = Path.GetFileName(op.Path.Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(opName, fileName, StringComparison.OrdinalIgnoreCase))
                op.Path = rel;
        }
    }

    // ══════════════════════════════════════════════
    //  VALIDATED APPLY WRAPPER
    // ══════════════════════════════════════════════
    static bool ApplyValidatedFiles(CodeWriterResult result, string baseDir, bool autoConfirm)
    {
        if (result == null || result.IsEmpty) return false;

        string details;
        if (ValidateOperationsViaAI2(result, out details))
            return ApplyGeneratedFiles(result, baseDir, autoConfirm);

        WriteColored(ConsoleColor.Red, "  \u2716 AI #2 validation: FAIL\n");
        if (!string.IsNullOrWhiteSpace(details)) RenderAssistantMessage(details);

        if (autoConfirm || ArcMode) {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 Авто-применение отменено валидацией.\n");
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  \u2753 Применить несмотря на FAIL? [y/N] ");
        Console.ResetColor();

        string confirm = Console.ReadLine();
        if (confirm != null && confirm.Trim().ToLowerInvariant() == "y")
            return ApplyGeneratedFiles(result, baseDir, true);

        WriteColored(ConsoleColor.DarkGray, "  \u25C2 Отменено.\n");
        return false;
    }
}