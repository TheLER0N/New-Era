// PlanHelpers.cs — хелперы плана: парсинг шагов, резолв путей, payload, парсинг блоков
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

partial class MainConsole
{
    static bool TryParsePlanStep(string step, out string action, out string file, out string desc)
    {
        action = null;
        file = null;
        desc = null;

        if (string.IsNullOrWhiteSpace(step))
            return false;

        string s = step.Trim();

        Match am = Regex.Match(s, @"^\[([^\]]+)\]\s*");
        string rest = s;

        if (am.Success)
        {
            action = am.Groups[1].Value.Trim();
            rest = s.Substring(am.Length);
        }

        Match m = Regex.Match(rest, @"^(.+?)\s*[\u2014\u2013]\s*(.*)$");
        if (!m.Success)
            m = Regex.Match(rest, @"^(.+?)\s+-\s+(.*)$");

        string head = rest.Trim();

        if (m.Success)
        {
            head = m.Groups[1].Value.Trim();
            desc = m.Groups[2].Value.Trim();
        }
        else
        {
            desc = rest.Trim();
        }

        if (string.IsNullOrEmpty(action))
            action = InferPlanAction(head);

        if (string.IsNullOrEmpty(action))
        {
            string full = (head + " " + (desc ?? "")).Trim();
            action = InferPlanAction(full);
        }

        if (string.IsNullOrEmpty(action))
            action = InferPlanAction(s);

        file = ExtractPlanFilePath(head, s);

        if (string.IsNullOrEmpty(action) && !string.IsNullOrEmpty(file))
            action = "MODIFY";

        if (!string.IsNullOrEmpty(action))
            action = NormalizePlanAction(action);

        return true;
    }

    static string InferPlanAction(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string t = text.ToUpperInvariant();

        if (t.Contains("УДАЛ") ||
            t.Contains("СНЕС") ||
            t.Contains("СТЕРЕТЬ") ||
            t.Contains("DELETE") ||
            t.Contains("REMOVE") ||
            t.Contains("ERASE"))
        {
            return "DELETE";
        }

        if (t.Contains("СОЗДА") ||
            t.Contains("ДОБАВ") ||
            t.Contains("CREATE") ||
            t.Contains("ADD"))
        {
            return "CREATE";
        }

        if (t.Contains("ОБНОВ") ||
            t.Contains("ИЗМЕН") ||
            t.Contains("ИСПРАВ") ||
            t.Contains("ПЕРЕПИС") ||
            t.Contains("ПОПРАВ") ||
            t.Contains("ПРАВК") ||
            t.Contains("UPDATE") ||
            t.Contains("EDIT") ||
            t.Contains("MODIFY") ||
            t.Contains("FIX") ||
            t.Contains("REWRITE"))
        {
            return "MODIFY";
        }

        if (t.Contains("ИЗУЧ") ||
            t.Contains("ПРОЧИТ") ||
            t.Contains("ЧТЕНИЕ") ||
            t.Contains("АНАЛИЗ") ||
            t.Contains("ПРОВЕР") ||
            t.Contains("ОПРЕДЕЛ") ||
            t.Contains("READ") ||
            t.Contains("ANALYZE") ||
            t.Contains("ANALYSE") ||
            t.Contains("INSPECT") ||
            t.Contains("REVIEW") ||
            t.Contains("STUDY"))
        {
            return "READ";
        }

        return null;
    }

    static string NormalizePlanAction(string rawAction)
    {
        if (string.IsNullOrWhiteSpace(rawAction))
            return null;

        string inferred = InferPlanAction(rawAction);
        if (!string.IsNullOrEmpty(inferred))
            return inferred;

        string a = rawAction.Trim().ToUpperInvariant();

        if (a.Contains("DELETE") || a.Contains("REMOVE") || a.Contains("ERASE"))
            return "DELETE";

        if (a.Contains("CREATE") || a.Contains("ADD"))
            return "CREATE";

        if (a.Contains("READ") || a.Contains("ANALYZE") || a.Contains("ANALYSE") ||
            a.Contains("INSPECT") || a.Contains("REVIEW") || a.Contains("STUDY"))
            return "READ";

        if (a.Contains("MODIFY") || a.Contains("EDIT") || a.Contains("UPDATE") || a.Contains("FIX"))
            return "MODIFY";

        return a;
    }

    static string ExtractPlanFilePath(string primary, string fallback)
    {
        string path = ExtractQuotedPath(primary);
        if (string.IsNullOrEmpty(path))
            path = ExtractQuotedPath(fallback);

        if (!string.IsNullOrEmpty(path))
            return CleanPlanFilePath(path);

        path = ExtractPathLikeToken(primary);
        if (string.IsNullOrEmpty(path))
            path = ExtractPathLikeToken(fallback);

        if (!string.IsNullOrEmpty(path))
            return CleanPlanFilePath(path);

        return null;
    }

    static string ExtractQuotedPath(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match m = Regex.Match(text, @"""([^""]+)""");
        if (m.Success)
            return m.Groups[1].Value.Trim();

        m = Regex.Match(text, @"'([^']+)'");
        if (m.Success)
            return m.Groups[1].Value.Trim();

        return null;
    }

    static string ExtractPathLikeToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string knownPattern =
            @"(?:[A-Za-z]:[\\/])?(?:[A-Za-z0-9_\-]+[\\/])*[A-Za-z0-9_\-]+\.(?:csproj|cs|bat|cmd|ps1|json|xml|sln|txt|cfg|ini|md|config|props|targets|exe|dll|pdb)";

        Match m = Regex.Match(text, knownPattern, RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Value;

        string dirPattern =
            @"(?:[A-Za-z]:[\\/])?[A-Za-z0-9_\-]+(?:[\\/][A-Za-z0-9_\-]+)+";

        m = Regex.Match(text, dirPattern, RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Value;

        return null;
    }

    static string CleanPlanFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.Trim();

        if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
            path = path.Substring(1, path.Length - 2);

        if (path.Length >= 2 && path[0] == '\'' && path[path.Length - 1] == '\'')
            path = path.Substring(1, path.Length - 2);

        path = path.Trim();

        while (path.Length > 0)
        {
            char last = path[path.Length - 1];

            if (last == ',' || last == ';' || last == ':' || last == '.' ||
                last == ')' || last == ']' || last == '}' || last == '"' || last == '\'')
            {
                path = path.Substring(0, path.Length - 1);
            }
            else
            {
                break;
            }
        }

        while (path.Length > 0)
        {
            char first = path[0];

            if (first == '"' || first == '\'')
                path = path.Substring(1);
            else
                break;
        }

        return path.Trim();
    }

    static bool IsEditableAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        string a = action.ToUpperInvariant();

        if (a.Contains("ПРАВКА") || a.Contains("ИСПРАВИТЬ") || a.Contains("ОБНОВИТЬ") ||
            a.Contains("ДОБАВИТЬ") || a.Contains("СОЗДАТЬ") || a.Contains("ИЗМЕНИТЬ") ||
            a.Contains("НАПИСАТЬ"))
            return true;

        if (a.Contains("FIX") || a.Contains("UPDATE") || a.Contains("EDIT") ||
            a.Contains("CREATE") || a.Contains("ADD") || a.Contains("MODIFY"))
            return true;

        return false;
    }

    static bool IsReadAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;

        string a = action.ToUpperInvariant();

        if (a.Contains("ИЗУЧИТЬ") || a.Contains("ПРОЧИТАТЬ") || a.Contains("АНАЛИЗ") ||
            a.Contains("ПРОСМОТРЕТЬ") || a.Contains("ОПРЕДЕЛИТЬ"))
            return true;

        if (a.Contains("READ") || a.Contains("ANALYZE") || a.Contains("ANALYSE") ||
            a.Contains("STUDY") || a.Contains("REVIEW") || a.Contains("INSPECT"))
            return true;

        return false;
    }

    static string GetProjectBaseDir(string projectPath)
    {
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

        return baseDir;
    }

    static string ResolvePlanFile(string file, string projectPath)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;

        string baseDir = GetProjectBaseDir(projectPath);

        string rel = file.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
        rel = rel.TrimStart(Path.DirectorySeparatorChar);

        try
        {
            string full = Path.IsPathRooted(rel)
                ? rel
                : Path.Combine(baseDir, rel);

            return Path.GetFullPath(full);
        }
        catch
        {
            return null;
        }
    }

    static List<string> CollectPlanFiles(List<string> steps, string projectPath)
    {
        var result = new List<string>();

        foreach (string step in steps)
        {
            string action, file, desc;
            TryParsePlanStep(step, out action, out file, out desc);

            if (string.IsNullOrWhiteSpace(file))
                continue;

            if (!IsEditableAction(action))
                continue;

            string full = ResolvePlanFile(file, projectPath);

            if (full != null && File.Exists(full) && !result.Contains(full))
                result.Add(full);
        }

        return result;
    }

    static string MakeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            string b = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string f = Path.GetFullPath(fullPath);

            if (f.StartsWith(b, StringComparison.OrdinalIgnoreCase))
                return f.Substring(b.Length).Replace(Path.DirectorySeparatorChar, '/');
        }
        catch
        {
        }

        return fullPath.Replace(Path.DirectorySeparatorChar, '/');
    }

    static string BuildPlanFilePayload(List<string> steps, string projectPath)
    {
        List<string> files = CollectPlanFiles(steps, projectPath);
        if (files.Count == 0)
            return null;

        string baseDir = GetProjectBaseDir(projectPath);

        var sb = new StringBuilder();
        long total = 0;

        const long maxTotal = MaxContextTotal;
        const int maxFile = MaxContextFile;

        foreach (string full in files)
        {
            string rel = MakeRelativePath(baseDir, full);

            string body;
            try
            {
                body = ReadTextAuto(full);
            }
            catch (Exception ex)
            {
                body = "// READ ERROR: " + ex.Message;
            }

            bool truncated = false;

            if (body.Length > maxFile)
            {
                body = body.Substring(0, maxFile);
                truncated = true;
            }

            if (total + body.Length > maxTotal)
            {
                sb.Append("\n=== FILE: " + rel + " ===\n");
                sb.Append("// [файл пропущен из-за общего лимита контекста]\n");
                sb.Append("=== END ===\n");
                continue;
            }

            total += body.Length;

            sb.Append("\n=== FILE: " + rel + " ===\n");
            sb.Append(body.TrimEnd('\r', '\n'));
            sb.Append("\n");

            if (truncated)
                sb.Append("// [файл обрезан до " + maxFile + " символов]\n");

            sb.Append("=== END ===\n");
        }

        return sb.ToString();
    }

    static Dictionary<string, string> ParsePlanFileBlocks(string text)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(text))
            return result;

        string[] lines = text.Split(new[] { "\n" }, StringSplitOptions.None);

        string currentFile = null;
        var content = new StringBuilder();

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();

            if (currentFile == null && trimmed.StartsWith("```"))
                continue;

            if (trimmed.StartsWith("=== FILE:") && trimmed.EndsWith("==="))
            {
                if (currentFile != null)
                    result[currentFile] = content.ToString().TrimEnd('\r', '\n');

                currentFile = trimmed.Substring(9, trimmed.Length - 12).Trim();
                content = new StringBuilder();
            }
            else if (trimmed == "=== END ===" && currentFile != null)
            {
                result[currentFile] = content.ToString().TrimEnd('\r', '\n');
                currentFile = null;
                content = new StringBuilder();
            }
            else if (currentFile != null)
            {
                content.Append(line);
                content.Append("\n");
            }
        }

        if (currentFile != null)
            result[currentFile] = content.ToString().TrimEnd('\r', '\n');

        return result;
    }
}