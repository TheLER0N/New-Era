// EditFolder.cs — редактирование папки (прямой путь, если dispatcher выключен)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    static void HandleEditFolder(string folderPath, string task)
    {
        string structure = ScanDirectory(folderPath, 0);
        string payload = BuildContextPayload(folderPath, 120000, 40000);

        var sb = new StringBuilder();

        sb.Append("Ты — редактор кода. Создай/измени файлы в папке.\n");
        sb.Append("Папка: " + folderPath + "\n");
        sb.Append("Задача: " + task + "\n");

        if (!string.IsNullOrEmpty(structure))
            sb.Append("Структура:\n" + structure + "\n");

        if (!string.IsNullOrEmpty(payload))
            sb.Append("\nCurrent source files:\n" + payload + "\n");

        sb.Append("\nВерни файлы блоками: === FILE: path === / === END ===\n");

        WriteColored(ConsoleColor.DarkGray,
            "  ◌ Отправка в ИИ (edit folder)...\n");

        AddHistory("user", "[edit-folder] " + folderPath + " " + task);

        StartSpinner("редактирование папки");

        string responseText = null;

        try
        {
            string raw = PostMessage(sb.ToString(), LastResponseId);
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ " + ex.Message + "\n");

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ.\n");
            return;
        }

        AddHistory("assistant", responseText);

        var files = ParseFileBlocks(responseText);

        if (files.Count == 0)
        {
            RenderAssistantMessage(responseText);
            return;
        }

        Console.WriteLine();

        foreach (var kv in files)
        {
            WriteColored(ConsoleColor.Cyan,
                "  ▸ " + kv.Key + " (" + kv.Value.Split('\n').Length + " строк)\n");
        }

        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("  ❓ Применить " + files.Count + " файл(ов)? [y/N] ");
        Console.ResetColor();

        string confirm = Console.ReadLine();

        if (confirm == null || confirm.Trim().ToLowerInvariant() != "y")
        {
            WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n");
            return;
        }

        int written = 0;

        foreach (var kv in files)
        {
            string outPath;

            if (!TryResolveSafeOutputPath(folderPath, kv.Key, out outPath))
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ " + kv.Key + ": путь вне проекта или недопустимый\n");

                LogChange(kv.Key, "MODIFY", "error");
                continue;
            }

            try
            {
                string dir = Path.GetDirectoryName(outPath);

                SaveRollbackSnapshot(outPath);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string content = kv.Value ?? "";

                if (!content.EndsWith("\n"))
                    content += "\n";

                File.WriteAllText(outPath, content, new UTF8Encoding(false));

                WriteColored(ConsoleColor.Green,
                    "  ✔ " + kv.Key + "\n");

                LogChange(outPath, "MODIFY", "success");

                written++;
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ " + kv.Key + ": " + ex.Message + "\n");

                LogChange(kv.Key, "MODIFY", "error");
            }
        }

        WriteColored(ConsoleColor.Green,
            "\n✔ Записано файлов: " + written + "\n");
    }
}