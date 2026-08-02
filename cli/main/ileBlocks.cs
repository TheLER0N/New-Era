// FileBlocks.cs — файловые блоки и локальный fallback-парсер
// New Era CLI v6.0
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>Операция над файлом.</summary>
class FileOperation
{
    public string Path;
    public string Action;   // CREATE, MODIFY, DELETE
    public string Content;

    public bool IsCreate { get { return Action != null && Action.ToUpperInvariant() == "CREATE"; } }
    public bool IsModify { get { return Action != null && Action.ToUpperInvariant() == "MODIFY"; } }
    public bool IsDelete { get { return Action != null && Action.ToUpperInvariant() == "DELETE"; } }
}

/// <summary>Результат парсинга файловых блоков.</summary>
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
    //  PARSE FILE/ACTION/CONTENT/END_FILE
    // ══════════════════════════════════════════════════════════
    static CodeWriterResult ParseCodeWriterResponse(string raw)
    {
        var result = new CodeWriterResult();
        result.RawText = raw ?? "";

        if (string.IsNullOrWhiteSpace(raw))
        {
            result.ValidationErrors.Add("Empty response");
            return result;
        }

        string cleaned = StripCodeWriterFences(raw);
        string[] lines = cleaned.Split(new[] { "\n" }, StringSplitOptions.None);

        string currentPath = null;
        string currentAction = null;
        var contentBuilder = new StringBuilder();
        bool inContent = false;
        bool hasAnyBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            string trimmed = line.Trim();

            if (trimmed.StartsWith("FILE:", StringComparison.OrdinalIgnoreCase))
            {
                if (currentPath != null)
                {
                    SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString(), inContent);
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
                SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString(), true);
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
                    "Block '" + currentPath + "' not closed with END_FILE");
            }

            SaveParsedBlock(result, currentPath, currentAction, contentBuilder.ToString(), inContent);
        }

        result.HasValidMarkers = hasAnyBlock && result.Operations.Count > 0;

        if (hasAnyBlock && result.Operations.Count == 0)
            result.ValidationErrors.Add("FILE marker found but no complete block");

        if (!hasAnyBlock)
            result.ValidationErrors.Add("No FILE/ACTION/CONTENT/END_FILE markers found");

        result.PlanConfirmed = result.Operations.Count > 0;
        return result;
    }

    static void SaveParsedBlock(
        CodeWriterResult result,
        string path,
        string action,
        string content,
        bool hadContent)
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
    //  STRIP FENCES
    // ══════════════════════════════════════════════════════════
    static string StripCodeWriterFences(string text)
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

        return t.TrimEnd('\r', '\n');
    }
}