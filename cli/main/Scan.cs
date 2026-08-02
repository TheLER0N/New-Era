// Scan.cs — команда /scan и сканирование структуры проекта
// New Era CLI v5.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  /scan <папка> — отчёт по структуре
    // ══════════════════════════════════════════════════════════
    static void HandleScan(string input)
    {
        string args = input.Length > 5 ? input.Substring(5).Trim() : "";
        if (string.IsNullOrEmpty(args))
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ Использование: scan <папка>\n");
            return;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(args.Trim('"')); }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  ✖ Недопустимый путь: " + ex.Message + "\n");
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            WriteColored(ConsoleColor.Red, "  ✖ Папка не найдена: " + fullPath + "\n");
            return;
        }

        string tree = ScanDirectory(fullPath, 0);
        lock (PrintLock)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╭─ ▸ СТРУКТУРА " + new string('─', 40) + "╮");
            Console.ResetColor();
            foreach (string line in tree.Split('\n'))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("  │ ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(line.TrimEnd('\r'));
            }
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╰" + new string('─', 54) + "╯");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    // ══════════════════════════════════════════════════════════
    //  SCAN DIRECTORY — рекурсивное дерево (для промптов)
    // ══════════════════════════════════════════════════════════
    static string ScanDirectory(string path, int depth)
    {
        if (depth > 6) return "";
        var sb = new StringBuilder();
        try
        {
            string[] dirs = Directory.GetDirectories(path);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(path);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            string indent = new string(' ', depth * 2);

            foreach (string d in dirs)
            {
                string name = Path.GetFileName(d);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
                string lower = name.ToLowerInvariant();
                if (lower == "bin" || lower == "obj" || lower == "node_modules" ||
                    lower == "program_from_the_cli" || lower == ".git" ||
                    lower == ".vs" || lower == ".vscode" || lower == ".idea")
                    continue;

                sb.Append(indent + "📁 " + name + "/\n");
                sb.Append(ScanDirectory(d, depth + 1));
            }

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
                long size = 0;
                try { size = new FileInfo(f).Length; } catch { }
                sb.Append(indent + "  " + name + " (" + size + " B)\n");
            }
        }
        catch { }
        return sb.ToString();
    }
}