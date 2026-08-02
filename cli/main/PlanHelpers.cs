// PlanHelpers.cs — хелперы плана: парсинг шагов, резолв путей, payload, парсинг блоков
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

partial class MainConsole
{
    static bool TryParsePlanStep(string step, out string action, out string file, out string desc) {
        action = null; file = null; desc = null;
        if (string.IsNullOrWhiteSpace(step)) return false;
        string s = step.Trim(); Match am = Regex.Match(s, @"^\[([^\]]+)\]\s*"); string rest = s;
        if (am.Success) { action = am.Groups[1].Value.Trim(); rest = s.Substring(am.Length); }
        Match m = Regex.Match(rest, @"^(.+?)\s*[\u2014\u2013]\s*(.*)$");
        if (!m.Success) m = Regex.Match(rest, @"^(.+?)\s+-\s+(.*)$");
        if (m.Success) { file = m.Groups[1].Value.Trim().Trim('"'); desc = m.Groups[2].Value.Trim(); } else { desc = rest.Trim(); }
        return true;
    }
    static bool IsEditableAction(string action) {
        if (string.IsNullOrWhiteSpace(action)) return false; string a = action.ToUpperInvariant();
        if (a.Contains("ПРАВКА") || a.Contains("ИСПРАВИТЬ") || a.Contains("ОБНОВИТЬ") || a.Contains("ДОБАВИТЬ") || a.Contains("СОЗДАТЬ") || a.Contains("ИЗМЕНИТЬ") || a.Contains("НАПИСАТЬ")) return true;
        if (a.Contains("FIX") || a.Contains("UPDATE") || a.Contains("EDIT") || a.Contains("CREATE") || a.Contains("ADD") || a.Contains("MODIFY")) return true;
        return false;
    }
    static bool IsReadAction(string action) {
        if (string.IsNullOrWhiteSpace(action)) return false; string a = action.ToUpperInvariant();
        if (a.Contains("ИЗУЧИТЬ") || a.Contains("ПРОЧИТАТЬ") || a.Contains("АНАЛИЗ") || a.Contains("ПРОСМОТРЕТЬ") || a.Contains("ОПРЕДЕЛИТЬ")) return true;
        if (a.Contains("READ") || a.Contains("ANALYZE") || a.Contains("ANALYSE") || a.Contains("STUDY") || a.Contains("REVIEW") || a.Contains("INSPECT")) return true;
        return false;
    }
    static string GetProjectBaseDir(string projectPath) { string baseDir = null; try { baseDir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath); } catch { } if (string.IsNullOrEmpty(baseDir)) baseDir = BaseDir; return baseDir; }
    static string ResolvePlanFile(string file, string projectPath) {
        if (string.IsNullOrWhiteSpace(file)) return null; string baseDir = GetProjectBaseDir(projectPath);
        string rel = file.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar); rel = rel.TrimStart(Path.DirectorySeparatorChar);
        try { string full = Path.IsPathRooted(rel) ? rel : Path.Combine(baseDir, rel); return Path.GetFullPath(full); } catch { return null; }
    }
    static List<string> CollectPlanFiles(List<string> steps, string projectPath) {
        var result = new List<string>();
        foreach (string step in steps) { string action, file, desc; TryParsePlanStep(step, out action, out file, out desc); if (string.IsNullOrWhiteSpace(file)) continue; if (!IsEditableAction(action)) continue; string full = ResolvePlanFile(file, projectPath); if (full != null && File.Exists(full) && !result.Contains(full)) result.Add(full); }
        return result;
    }
    static string MakeRelativePath(string baseDir, string fullPath) {
        try { string b = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; string f = Path.GetFullPath(fullPath); if (f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return f.Substring(b.Length).Replace(Path.DirectorySeparatorChar, '/'); } catch { }
        return fullPath.Replace(Path.DirectorySeparatorChar, '/');
    }
    static string BuildPlanFilePayload(List<string> steps, string projectPath) {
        List<string> files = CollectPlanFiles(steps, projectPath); if (files.Count == 0) return null;
        string baseDir = GetProjectBaseDir(projectPath); var sb = new StringBuilder(); long total = 0; const long maxTotal = MaxContextTotal; const int maxFile = MaxContextFile;
        foreach (string full in files) {
            string rel = MakeRelativePath(baseDir, full); string body; try { body = ReadTextAuto(full); } catch (Exception ex) { body = "// READ ERROR: " + ex.Message; }
            bool truncated = false; if (body.Length > maxFile) { body = body.Substring(0, maxFile); truncated = true; }
            if (total + body.Length > maxTotal) { sb.Append("\n=== FILE: " + rel + " ===\n"); sb.Append("// [файл пропущен из-за общего лимита контекста]\n"); sb.Append("=== END ===\n"); continue; }
            total += body.Length; sb.Append("\n=== FILE: " + rel + " ===\n"); sb.Append(body.TrimEnd('\r', '\n')); sb.Append("\n");
            if (truncated) sb.Append("// [файл обрезан до " + maxFile + " символов]\n"); sb.Append("=== END ===\n");
        }
        return sb.ToString();
    }
    static Dictionary<string, string> ParsePlanFileBlocks(string text) {
        var result = new Dictionary<string, string>(); if (string.IsNullOrEmpty(text)) return result;
        string[] lines = text.Split(new[] { "\n" }, StringSplitOptions.None); string currentFile = null; var content = new StringBuilder();
        foreach (string rawLine in lines) {
            string line = rawLine.TrimEnd('\r'); string trimmed = line.Trim();
            if (currentFile == null && trimmed.StartsWith("```")) continue;
            if (trimmed.StartsWith("=== FILE:") && trimmed.EndsWith("===")) { if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n'); currentFile = trimmed.Substring(9, trimmed.Length - 12).Trim(); content = new StringBuilder(); }
            else if (trimmed == "=== END ===" && currentFile != null) { result[currentFile] = content.ToString().TrimEnd('\r', '\n'); currentFile = null; content = new StringBuilder(); }
            else if (currentFile != null) { content.Append(line); content.Append("\n"); }
        }
        if (currentFile != null) result[currentFile] = content.ToString().TrimEnd('\r', '\n');
        return result;
    }
}