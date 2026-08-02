// EditMode.cs — режим редактирования файлов через ИИ (entry point + прямой путь)
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// Рефакторинг v5.2: файл раздроблен по зонам ответственности (100–300 строк на файл).
//   EditMode.cs           — HandleEdit (entry), HandleEditFile (прямой путь), shared-хелперы
//   EditFileTwoLevel.cs   — HandleEditFileTwoLevel, PostValidateWrittenFile, ResolveEditPath
//   EditFolder.cs         — HandleEditFolder, HandleEditFolderTwoLevel
//   Guardian.cs           — константы/промпты Guardian, GuardianAnalysis, ParseGuardianAnalysis,
//                           IsGuardianPass, ExtractGuardianErrors, ExtractGuardianCoordinates
//
// Двухуровневый протокол (при GuardianEnabled = true):
//   ШАГ 1: SYSTEM_GUARDIAN анализирует запрос → ENHANCED_TASK / TARGET_FILES / ACCEPTANCE.
//   ШАГ 2: CODE_WRITER генерирует код по плану.
//   ШАГ 3: SYSTEM_GUARDIAN валидирует ответ.
//   ШАГ 4: Применение + ПОСТ-валидация по фактическому содержимому на диске.
//   Аркест-режим (ArcMode): авто-применение без подтверждения.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  EDIT MODE (entry point)
    // ══════════════════════════════════════════════════════════
    static void HandleEdit(string input)
    {
        string args = input.Length > 5 ? input.Substring(5).Trim() : "";
        if (string.IsNullOrEmpty(args))
        {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 \u0418\u0441\u043F\u043E\u043B\u044C\u0437\u043E\u0432\u0430\u043D\u0438\u0435: edit <\u0444\u0430\u0439\u043B> [N-M] <\u0437\u0430\u0434\u0430\u0447\u0430>\n");
            WriteColored(ConsoleColor.DarkGray, "               edit <\u043F\u0430\u043F\u043A\u0430> <\u0437\u0430\u0434\u0430\u0447\u0430>\n");
            return;
        }

        string targetPath;
        string rangeStr = null;
        string task = null;

        if (args.StartsWith("\""))
        {
            int close = args.IndexOf('"', 1);
            if (close > 0)
            {
                targetPath = args.Substring(1, close - 1);
                string rest = args.Substring(close + 1).Trim();
                if (Regex.IsMatch(rest, @"^\d+-\d+\s"))
                {
                    int sp = rest.IndexOf(' ');
                    rangeStr = sp > 0 ? rest.Substring(0, sp) : rest;
                    task = sp > 0 ? rest.Substring(sp + 1).Trim() : "";
                }
                else
                {
                    task = rest;
                }
            }
            else
            {
                targetPath = args.Substring(1).TrimEnd('"');
                task = "";
            }
        }
        else
        {
            string[] parts = args.Split(new[] { ' ' }, 3);
            targetPath = parts[0];
            if (parts.Length >= 2)
            {
                if (Regex.IsMatch(parts[1], @"^\d+-\d+$"))
                {
                    rangeStr = parts[1];
                    task = parts.Length >= 3 ? parts[2].Trim() : "";
                }
                else
                {
                    task = args.Substring(parts[0].Length).Trim();
                }
            }
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(targetPath); }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  \u2716 \u041D\u0435\u0434\u043E\u043F\u0443\u0441\u0442\u0438\u043C\u044B\u0439 \u043F\u0443\u0442\u044C: " + ex.Message + "\n");
            return;
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.DarkGray, "  \u25CC \u0412\u0432\u0435\u0434\u0438 \u0437\u0430\u0434\u0430\u0447\u0443 (\u043F\u0443\u0441\u0442\u0430\u044F \u0441\u0442\u0440\u043E\u043A\u0430 = \u043A\u043E\u043D\u0435\u0446):\n");
            task = ReadMultiline();
        }
        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 \u041F\u0443\u0441\u0442\u0430\u044F \u0437\u0430\u0434\u0430\u0447\u0430. \u041E\u0442\u043C\u0435\u043D\u0430.\n");
            return;
        }

        if (Directory.Exists(fullPath))
            HandleEditFolder(fullPath, task);
        else if (File.Exists(fullPath))
            HandleEditFile(fullPath, rangeStr, task);
        else
            WriteColored(ConsoleColor.Red, "  \u2716 \u041F\u0443\u0442\u044C \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D: " + fullPath + "\n");
    }

    // ══════════════════════════════════════════════════════════
    //  HANDLE EDIT FILE (two-level aware)
    // ══════════════════════════════════════════════════════════
    static bool HandleEditFile(string filePath, string rangeStr, string task, bool autoConfirm = false)
    {
        string[] allLines;
        try
        {
            string fileContent = ReadTextAuto(filePath);
            allLines = fileContent.Split(new[] { "\n" }, StringSplitOptions.None);
            for (int li = 0; li < allLines.Length; li++)
                allLines[li] = allLines[li].TrimEnd('\r');
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  \u2716 \u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u043F\u0440\u043E\u0447\u0438\u0442\u0430\u0442\u044C: " + ex.Message + "\n");
            return false;
        }

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

        var sb = new StringBuilder();
        for (int i = startLine; i <= endLine; i++)
            sb.AppendLine((i + 1).ToString().PadLeft(4) + " | " + allLines[i]);
        string fragment = sb.ToString();

        // ── Маршрутизация: двухуровневый или прямой путь ──
        // v5.1: улучшение задачи делает Guardian (не Orchestrator) при GuardianEnabled.
        if (GuardianEnabled)
        {
            return HandleEditFileTwoLevel(filePath, rangeStr, task, fragment,
                allLines, startLine, endLine, autoConfirm);
        }

        // Прямой путь (без Guardian): Orchestrator может улучшить задачу.
        string effectiveTask = task;
        if (OrchestratorEnabled)
        {
            try
            {
                string enhanced = EnhancePrompt(task);
                if (!string.IsNullOrWhiteSpace(enhanced))
                {
                    effectiveTask = enhanced;
                    WriteColored(ConsoleColor.DarkGray, "  \u25CC orchestrator: enhanced\n");
                }
            }
            catch (Exception orchEx)
            {
                WriteColored(ConsoleColor.Yellow, "  \u26A0 orchestrator: bypassed (" + orchEx.Message + ")\n");
            }
        }

        string prompt =
            "\u0422\u044B \u2014 \u0440\u0435\u0434\u0430\u043A\u0442\u043E\u0440 \u043A\u043E\u0434\u0430. \u041E\u0442\u0440\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u0443\u0439 \u0444\u0440\u0430\u0433\u043C\u0435\u043D\u0442 \u0444\u0430\u0439\u043B\u0430 \u043F\u043E \u0437\u0430\u0434\u0430\u0447\u0435.\n" +
            "\u0424\u0430\u0439\u043B: " + Path.GetFileName(filePath) + "\n" +
            (rangeStr != null ? "\u0414\u0438\u0430\u043F\u0430\u0437\u043E\u043D \u0441\u0442\u0440\u043E\u043A: " + rangeStr + "\n" : "") +
            "\u0417\u0430\u0434\u0430\u0447\u0430: " + effectiveTask + "\n" +
            "\u0422\u0435\u043A\u0443\u0449\u0438\u0439 \u043A\u043E\u0434:\n```\n" + fragment + "```\n" +
            "\u0412\u0435\u0440\u043D\u0438 \u0422\u041E\u041B\u042C\u041A\u041E \u043D\u043E\u0432\u044B\u0439 \u043A\u043E\u0434 (\u0431\u0435\u0437 \u043F\u043E\u044F\u0441\u043D\u0435\u043D\u0438\u0439, \u0431\u0435\u0437 ```).";

        WriteColored(ConsoleColor.DarkGray, "  \u25CC \u041E\u0442\u043F\u0440\u0430\u0432\u043A\u0430 \u0432 \u0418\u0418 (edit: " + Path.GetFileName(filePath) + ")...\n");
        AddHistory("user", "[edit] " + filePath + " " + task);
        StartSpinner("\u0440\u0435\u0434\u0430\u043A\u0442\u0438\u0440\u043E\u0432\u0430\u043D\u0438\u0435");
        string responseText = null;
        try
        {
            string raw = PostMessage(prompt, LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();
            WriteColored(ConsoleColor.Red, "  \u2716 \u041E\u0448\u0438\u0431\u043A\u0430: " + ex.Message + "\n");
            return false;
        }
        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 \u041F\u0443\u0441\u0442\u043E\u0439 \u043E\u0442\u0432\u0435\u0442.\n");
            return false;
        }

        responseText = StripCodeFence(responseText);
        string[] newLines = responseText.Split(new[] { "\n" }, StringSplitOptions.None);
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
            WriteColored(ConsoleColor.Green, "  \u2714 \u0410\u0432\u0442\u043E-\u0437\u0430\u043F\u0438\u0441\u044C (\u0431\u0435\u0437 \u043F\u043E\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043D\u0438\u044F)\n");
            doWrite = true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  \u2753 \u0417\u0430\u043F\u0438\u0441\u0430\u0442\u044C \u0438\u0437\u043C\u0435\u043D\u0435\u043D\u0438\u044F? [y/N] ");
            Console.ResetColor();
            string confirm = Console.ReadLine();
            doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y";
        }

        if (doWrite)
        {
            try
            {
                var result = new List<string>();
                for (int i = 0; i < startLine; i++) result.Add(allLines[i]);
                foreach (string nl in newLines) result.Add(nl.TrimEnd('\r'));
                for (int i = endLine + 1; i < allLines.Length; i++) result.Add(allLines[i]);
                string finalContent = string.Join("\n", result.ToArray());
                if (!finalContent.EndsWith("\n")) finalContent += "\n";
                File.WriteAllText(filePath, finalContent, new UTF8Encoding(false));
                WriteColored(ConsoleColor.Green, "  \u2714 \u0417\u0430\u043F\u0438\u0441\u0430\u043D\u043E: " + filePath +
                    " (" + finalContent.Length + " \u0441\u0438\u043C\u0432\u043E\u043B\u043E\u0432)\n");
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Red, "  \u2716 \u041E\u0448\u0438\u0431\u043A\u0430 \u0437\u0430\u043F\u0438\u0441\u0438: " + ex.Message + "\n");
                AddHistory("assistant", responseText);
                return false;
            }
        }
        else
        {
            WriteColored(ConsoleColor.DarkGray, "  \u25C2 \u041E\u0442\u043C\u0435\u043D\u0435\u043D\u043E.\n");
            AddHistory("assistant", responseText);
            return false;
        }

        AddHistory("assistant", responseText);
        return true;
    }

    // ══════════════════════════════════════════════════════════
    //  SHARED HELPERS
    // ══════════════════════════════════════════════════════════
    static string ReadMultiline()
    {
        var sb = new StringBuilder();
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  > ");
            Console.ResetColor();
            string line;
            try { line = Console.ReadLine(); }
            catch { break; }
            if (line == null || line.Trim().Length == 0) break;
            if (sb.Length > 0) sb.Append("\n");
            sb.Append(line);
        }
        return sb.ToString();
    }

    static string StripCodeFence(string text)
    {
        if (text == null) return "";
        string t = text.Trim();
        if (t.StartsWith("```"))
        {
            int firstNl = t.IndexOf('\n');
            if (firstNl >= 0) t = t.Substring(firstNl + 1);
            if (t.EndsWith("```"))
                t = t.Substring(0, t.Length - 3);
        }
        return t.TrimEnd('\r', '\n');
    }

    static Dictionary<string, string> ParseFileBlocks(string text)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(text)) return result;
        string[] lines = text.Split(new[] { "\n" }, StringSplitOptions.None);
        string currentFile = null;
        var content = new StringBuilder();
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();
            if (currentFile == null && trimmed.StartsWith("```")) continue;
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

    static void ShowDiff(string[] original, int start, int end, string[] newLines)
    {
        lock (PrintLock)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u256D\u2500 \u25B8 DIFF " + new string('\u2500', 40) + "\u256E");
            Console.ResetColor();
            var oldSet = new HashSet<string>();
            for (int i = start; i <= end; i++)
                oldSet.Add(original[i].TrimEnd('\r'));
            var newSet = new HashSet<string>();
            foreach (string nl in newLines)
                newSet.Add(nl.TrimEnd('\r'));
            for (int i = start; i <= end; i++)
            {
                string ol = original[i].TrimEnd('\r');
                if (!newSet.Contains(ol))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  \u2502 - " + ol);
                }
            }
            foreach (string nl in newLines)
            {
                string trimmed = nl.TrimEnd('\r');
                if (!oldSet.Contains(trimmed))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  \u2502 + " + trimmed);
                }
            }
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  \u2570" + new string('\u2500', 46) + "\u256F");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
