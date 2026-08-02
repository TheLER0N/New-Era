// EditMode.cs — режим редактирования файлов через ИИ (entry point + прямой путь)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

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
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Использование: edit <файл> [N-M] <задача>\n");
            WriteColored(ConsoleColor.DarkGray,
                "               edit <папка> <задача>\n");
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

        try
        {
            fullPath = Path.GetFullPath(targetPath);
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Недопустимый путь: " + ex.Message + "\n");
            return;
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.DarkGray,
                "  ◌ Введи задачу (пустая строка = конец):\n");

            task = ReadMultiline();
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустая задача. Отмена.\n");
            return;
        }

        if (Directory.Exists(fullPath))
        {
            if (DispatcherEnabled)
                HandleEditFolderV6(fullPath, task);
            else
                HandleEditFolder(fullPath, task);
        }
        else if (File.Exists(fullPath))
        {
            if (DispatcherEnabled)
                HandleEditFileV6(fullPath, rangeStr, task, false);
            else
                HandleEditFile(fullPath, rangeStr, task, false);
        }
        else
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Путь не найден: " + fullPath + "\n");
        }
    }

    // ══════════════════════════════════════════════════════════
    //  HANDLE EDIT FILE (прямой путь, если dispatcher выключен)
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
            WriteColored(ConsoleColor.Red,
                "  ✖ Не удалось прочитать: " + ex.Message + "\n");
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

        string prompt =
            "Ты — редактор кода. Отредактируй фрагмент файла по задаче.\n" +
            "Файл: " + Path.GetFileName(filePath) + "\n" +
            (rangeStr != null ? "Диапазон строк: " + rangeStr + "\n" : "") +
            "Задача: " + task + "\n" +
            "Текущий код:\n```\n" + fragment + "```\n" +
            "Верни ТОЛЬКО новый код (без пояснений, без ```).";

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Отправка в ИИ (edit: " + Path.GetFileName(filePath) + ")...\n");

        AddHistory("user", "[edit] " + filePath + " " + task);

        StartSpinner("редактирование");

        string responseText = null;

        try
        {
            string raw = PostMessage(prompt, LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + "\n");

            return false;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");
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
            WriteColored(ConsoleColor.Green,
                "  ✔ Авто-запись (без подтверждения)\n");
            doWrite = true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  ❓ Записать изменения? [y/N] ");
            Console.ResetColor();

            string confirm = Console.ReadLine();
            doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y";
        }

        if (doWrite)
        {
            try
            {
                var result = new List<string>();

                for (int i = 0; i < startLine; i++)
                    result.Add(allLines[i]);

                foreach (string nl in newLines)
                    result.Add(nl.TrimEnd('\r'));

                for (int i = endLine + 1; i < allLines.Length; i++)
                    result.Add(allLines[i]);

                string finalContent = string.Join("\n", result.ToArray());

                if (!finalContent.EndsWith("\n"))
                    finalContent += "\n";

                SaveRollbackSnapshot(filePath);
                File.WriteAllText(filePath, finalContent, new UTF8Encoding(false));

                WriteColored(ConsoleColor.Green,
                    "  ✔ Записано: " + filePath +
                    " (" + finalContent.Length + " символов)\n");

                LogChange(filePath, "MODIFY", "success");
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ Ошибка записи: " + ex.Message + "\n");

                LogChange(filePath, "MODIFY", "error");
                AddHistory("assistant", responseText);

                return false;
            }
        }
        else
        {
            WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n");
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

            try
            {
                line = Console.ReadLine();
            }
            catch
            {
                break;
            }

            if (line == null || line.Trim().Length == 0)
                break;

            if (sb.Length > 0)
                sb.Append("\n");

            sb.Append(line);
        }

        return sb.ToString();
    }

    static string StripCodeFence(string text)
    {
        if (text == null)
            return "";

        string t = text.Trim();

        if (t.StartsWith("```"))
        {
            int firstNl = t.IndexOf('\n');

            if (firstNl >= 0)
                t = t.Substring(firstNl + 1);

            if (t.EndsWith("```"))
                t = t.Substring(0, t.Length - 3);
        }

        return t.TrimEnd('\r', '\n');
    }

    static Dictionary<string, string> ParseFileBlocks(string text)
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

    static void ShowDiff(string[] original, int start, int end, string[] newLines)
    {
        lock (PrintLock)
        {
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭─ ▸ DIFF " + new string('─', 40) + "╮");
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
                    Console.WriteLine("  │ - " + ol);
                }
            }

            foreach (string nl in newLines)
            {
                string trimmed = nl.TrimEnd('\r');

                if (!oldSet.Contains(trimmed))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  │ + " + trimmed);
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╰" + new string('─', 46) + "╯");
            Console.ResetColor();

            Console.WriteLine();
        }
    }
}