// FileOps.cs — применение файлов, парсер, rollback, diff
// New Era v7.2
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
partial class MainConsole
{
static bool ApplyValidatedFiles(CodeWriterResult result, string projectPath, bool approved)
{
if (result == null || result.IsEmpty) return false;
string details;
if (!ValidateOperationsViaAI2(result, out details)) {
WriteColored(ConsoleColor.Red, " \u2716 AI #2 validator: FAIL\n");
if (!string.IsNullOrEmpty(details)) WriteColored(ConsoleColor.DarkGray, details + "\n");
return false;
}
string baseDir = GetProjectBaseDir(projectPath);
int written = 0;
foreach (var op in result.Operations) {
if (string.IsNullOrWhiteSpace(op.Path)) continue;
string outPath;
if (!TryResolveSafeOutputPath(baseDir, op.Path, out outPath)) { WriteColored(ConsoleColor.Red, " \u2716 Путь вне проекта: " + op.Path + "\n"); continue; }
if (op.Action != null && op.Action.ToUpperInvariant() == "DELETE") {
if (File.Exists(outPath)) {
if (!approved) { Console.ForegroundColor = ConsoleColor.Red; Console.Write(" \u2753 Удалить " + outPath + "? [y/N] "); Console.ResetColor(); string c = Console.ReadLine(); if (c == null || c.Trim().ToLowerInvariant() != "y") continue; }
try { SaveRollbackSnapshot(outPath); File.Delete(outPath); WriteColored(ConsoleColor.Red, " \u2716 DELETE " + outPath + "\n"); LogChange(outPath, "DELETE", "success"); } catch (Exception ex) { WriteColored(ConsoleColor.Red, " \u2716 " + ex.Message + "\n"); }
}
continue;
}
string content = op.Content ?? "";
if (string.IsNullOrWhiteSpace(content)) continue;
if (!approved) {
WriteColored(ConsoleColor.Yellow, " \u2753 " + (op.Action ?? "MODIFY") + " " + outPath + "? [y/N] ");
string c = Console.ReadLine(); if (c == null || c.Trim().ToLowerInvariant() != "y") continue;
}
try {
string dir = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
if (File.Exists(outPath)) SaveRollbackSnapshot(outPath);
File.WriteAllText(outPath, content, new UTF8Encoding(false));
WriteColored(ConsoleColor.Green, " \u2714 " + (op.Action ?? "MODIFY") + " " + outPath + "\n");
LogChange(outPath, op.Action ?? "MODIFY", "success"); written++;
} catch (Exception ex) { WriteColored(ConsoleColor.Red, " \u2716 " + ex.Message + "\n"); LogChange(outPath, op.Action, "error"); }
}
return written > 0;
}
static CodeWriterResult ParseCodeWriterResponse(string raw)
{
    var result = new CodeWriterResult { RawText = raw ?? "" };
    if (string.IsNullOrWhiteSpace(raw)) return result;
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
            if (currentPath != null) SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString());
            currentPath = trimmed.Substring(5).Trim().Trim('"');
            currentAction = null; contentBuilder = new StringBuilder(); inContent = false; hasAnyBlock = true; continue;
        }
        if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase) && currentPath != null) {
            currentAction = trimmed.Substring(7).Trim().ToUpperInvariant(); continue;
        }
        if (trimmed.StartsWith("CONTENT:", StringComparison.OrdinalIgnoreCase) && currentPath != null) { inContent = true; continue; }
        if (trimmed.StartsWith("END_FILE", StringComparison.OrdinalIgnoreCase)) {
            if (currentPath != null) SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString());
            currentPath = null; currentAction = null; inContent = false; continue;
        }
        if (inContent && currentPath != null) contentBuilder.Append(line).Append("\n");
    }
    if (currentPath != null) SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString());
    return result;
}

static void SaveParsedBlock(CodeWriterResult result, string path, string action, string content)
{
    if (string.IsNullOrWhiteSpace(path)) return;
    result.Operations.Add(new CodeOperation { Path = path.Replace('\\', '/'), Action = string.IsNullOrEmpty(action) ? "MODIFY" : action, Content = content.TrimEnd('\r', '\n') + "\n" });
}

static void NormalizeSingleFileOperation(CodeWriterResult result, string filePath, string projectPath)
{
    if (result == null || result.IsEmpty || string.IsNullOrEmpty(filePath)) return;
    string fileName = Path.GetFileName(filePath);
    string rel = MakeRelativePath(projectPath, filePath).Replace('\\', '/');
    if (result.Operations.Count == 1) { result.Operations[0].Path = rel; if (string.IsNullOrEmpty(result.Operations[0].Action)) result.Operations[0].Action = "MODIFY"; return; }
    for (int i = 0; i < result.Operations.Count; i++) {
        string p = result.Operations[i].Path ?? "";
        if (p.Equals(rel, StringComparison.OrdinalIgnoreCase) || p.Equals(fileName, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase)) {
            var op = result.Operations[i]; result.Operations.Clear(); result.Operations.Add(op); result.Operations[0].Path = rel; return;
        }
    }
    var first = result.Operations[0]; result.Operations.Clear(); result.Operations.Add(first); result.Operations[0].Path = rel;
}

static bool TryResolveSafeOutputPath(string baseDir, string relPath, out string fullPath)
{
    fullPath = null;
    if (string.IsNullOrWhiteSpace(relPath) || string.IsNullOrWhiteSpace(baseDir)) return false;
    relPath = relPath.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    if (relPath.Contains("..")) return false;
    try {
        string rootedFull = Path.IsPathRooted(relPath) ? relPath : Path.Combine(baseDir, relPath);
        rootedFull = Path.GetFullPath(rootedFull);
        string fullBase = Path.GetFullPath(baseDir);
        if (!rootedFull.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase)) return false;
        fullPath = rootedFull; return true;
    } catch { return false; }
}

static string MakeRelativePath(string baseDir, string fullPath)
{
    try {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(fullPath)) return fullPath ?? "";
        string fullBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string rootedFull = Path.GetFullPath(fullPath);
        if (!rootedFull.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(fullPath);
        string rel = rootedFull.Substring(fullBase.Length);
        return string.IsNullOrWhiteSpace(rel) ? Path.GetFileName(fullPath) : rel.Replace(Path.DirectorySeparatorChar, '/');
    } catch { return Path.GetFileName(fullPath); }
}

static string ReadTextAuto(string path)
{
    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
    byte[] raw; try { raw = File.ReadAllBytes(path); } catch { return ""; }
    if (raw == null || raw.Length == 0) return "";
    Encoding enc; int skip = 0;
    if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) { enc = Encoding.UTF8; skip = 3; }
    else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE) { enc = Encoding.Unicode; skip = 2; }
    else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF) { enc = Encoding.BigEndianUnicode; skip = 2; }
    else enc = Encoding.UTF8;
    string result; try { result = enc.GetString(raw, skip, raw.Length - skip); } catch { result = Encoding.UTF8.GetString(raw, skip, raw.Length - skip); }
    if (result.Length > 0 && result[0] == '\uFEFF') result = result.Substring(1);
    return result.Replace("\0", "");
}

static void SaveRollbackSnapshot(string filePath)
{
    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
    try {
        string content = ReadTextAuto(filePath);
        lock (HistoryLock) {
            RollbackHistory.Add(new RollbackEntry { Path = filePath, Content = content, Time = DateTime.Now });
            while (RollbackHistory.Count > MaxRollbackEntries) RollbackHistory.RemoveAt(0);
        }
    } catch { }
}

static void LogChange(string file, string action, string status)
{
    string entry = "[" + DateTime.Now.ToString("dd.MM HH:mm:ss") + "] FILE: " + (file ?? "?") + "| ACTION: " + (action ?? "MODIFY") + "| STATUS: " + (status ?? "?");
    lock (ChangeLog) { ChangeLog.Add(entry); while (ChangeLog.Count > MaxChangeLogEntries) ChangeLog.RemoveAt(0); }
}

static void ShowDiff(string[] oldLines, int startLine, int endLine, string[] newLines)
{
    lock (PrintLock) {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u256D\u2500 \u25B8 DIFF " + new string('\u2500', 30) + "\u256E"); Console.ResetColor();
        for (int i = startLine; i <= endLine && i < oldLines.Length; i++) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("  \u2502 - " + oldLines[i]); }
        foreach (string nl in newLines) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  \u2502 + " + nl); }
        Console.ForegroundColor = ConsoleColor.DarkCyan; Console.WriteLine("  \u2570" + new string('\u2500', 44) + "\u256F"); Console.ResetColor();
        Console.WriteLine();
    }
}

static string EscapeJson(string s)
{
    if (s == null) return "\"\"";
    var sb = new StringBuilder("\"");
    foreach (char c in s) {
        switch (c) {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            default: if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4")); else sb.Append(c); break;
        }
    }
    sb.Append("\""); return sb.ToString();
}

static string JsonStr(string s) { return EscapeJson(s ?? ""); }

static string ResolveProjectDirectory(string startDir)
{
    if (string.IsNullOrEmpty(startDir)) return BaseDir;
    try {
        string fallback = startDir; var dir = new DirectoryInfo(startDir);
        while (dir != null) {
            if (dir.Exists) {
                if (fallback == null) fallback = dir.FullName;
                if (LooksLikeProjectRoot(dir.FullName)) return dir.FullName;
            }
            dir = dir.Parent;
        }
        return fallback;
    } catch { return BaseDir; }
}

static bool LooksLikeProjectRoot(string dir)
{
    try {
        if (Directory.GetFiles(dir, "*.csproj").Length > 0) return true;
        if (Directory.GetFiles(dir, "*.sln").Length > 0) return true;
        if (Directory.Exists(Path.Combine(dir, ".git"))) return true;
        if (File.Exists(Path.Combine(dir, "plan.txt"))) return true;
    } catch { }
    return false;
}
}
class CodeWriterResult
{
public string RawText;
public List<CodeOperation> Operations = new List<CodeOperation>();
public bool IsEmpty { get { return Operations == null || Operations.Count == 0; } }
}
class CodeOperation
{
public string Path;
public string Action;
public string Content;
public bool IsDelete { get { return Action != null && Action.ToUpperInvariant() == "DELETE"; } }
}