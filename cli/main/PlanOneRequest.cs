// PlanOneRequest.cs — выполнение всего плана за 1 запрос (прямой путь, если dispatcher выключен)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    static void ExecutePlanOneRequest(
        List<string> steps,
        string projectPath,
        string originalTask,
        string structure)
    {
        if (DispatcherEnabled)
        {
            ExecutePlanOneRequestV6(steps, projectPath, originalTask, structure);
            return;
        }

        string NL = Environment.NewLine;

        var sb = new StringBuilder();

        sb.Append("Ты — редактор кода. Выполни весь план за один проход." + NL);
        sb.Append("Проект: " + projectPath + NL);

        if (!string.IsNullOrWhiteSpace(originalTask))
            sb.Append("Задача: " + originalTask + NL);

        if (!string.IsNullOrWhiteSpace(structure))
            sb.Append("Структура проекта:" + NL + structure + NL);

        sb.Append(NL + "План:" + NL);

        for (int i = 0; i < steps.Count; i++)
            sb.Append((i + 1) + ". " + steps[i] + NL);

        sb.Append(NL + "Правила:" + NL);

        sb.Append(
            "- Меняешь файл — верни его ПОЛНОСТЬЮ блоком:" + NL +
            "=== FILE: путь/относительно/проекта ===" + NL +
            "содержимое" + NL +
            "=== END ===" + NL);

        sb.Append("- Шаги без файлов опиши одной строкой в начале ответа." + NL);
        sb.Append("- Не добавляй ничего сверх плана." + NL);
        sb.Append("- Если файл не меняется, не возвращай его." + NL);

        string payload = BuildPlanFilePayload(steps, projectPath);

        if (string.IsNullOrEmpty(payload))
            payload = BuildContextPayload(projectPath, MaxContextTotal, MaxContextFile);

        if (!string.IsNullOrEmpty(payload))
        {
            sb.Append(NL + "Текущие исходные файлы для правки:" + NL);
            sb.Append(payload);
            sb.Append(NL + "Верни изменённые файлы в таких же блоках, сохраняя относительные пути." + NL);
        }
        else
        {
            sb.Append(
                NL +
                "Если для правки нужны исходные файлы, которых нет в запросе, " +
                "не выдумывай: верни одну строку NEED FILES: список путей." + NL);
        }

        WriteColored(ConsoleColor.DarkGray,
            " ◌ Выполнение плана за 1 запрос (" +
            steps.Count + " " + StepsWord(steps.Count) + ")..." + NL);

        AddHistory("user", "[plan-exec] " + (originalTask ?? ""));

        StartSpinner("выполнение (1 запрос)");

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
                "  ✖ Ошибка: " + ex.Message + NL);

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                " ⚠ Пустой ответ." + NL);

            return;
        }

        AddHistory("assistant", responseText);

        var files = ParsePlanFileBlocks(responseText);

        if (files.Count == 0)
        {
            WriteColored(ConsoleColor.Yellow,
                " ⚠ Файлов в ответе нет — показываю ответ:" + NL);

            RenderAssistantMessage(responseText);

            return;
        }

        Console.WriteLine();

        foreach (var kv in files)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;

            Console.WriteLine(
                " ▸ " + kv.Key + " (" +
                kv.Value.Split(new[] { (char)10 }).Length + " строк)");
        }

        Console.ResetColor();

        Console.WriteLine();

        string confirm;

        if (ArcMode)
        {
            WriteColored(ConsoleColor.Green,
                " ✔ Аркест: авто-применение" + NL);

            confirm = "y";
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(" ❓ Применить план: " + files.Count + " файл(ов)? [y/N] ");
            Console.ResetColor();

            confirm = Console.ReadLine();
        }

        if (confirm == null || confirm.Trim().ToLowerInvariant() != "y")
        {
            WriteColored(ConsoleColor.DarkGray,
                "  ◂ Отменено." + NL);

            return;
        }

        string baseDir = GetProjectBaseDir(projectPath);
        string lineBreak = new string((char)10, 1);

        int written = 0;

        foreach (var kv in files)
        {
            string outPath;

            if (!TryResolveSafeOutputPath(baseDir, kv.Key, out outPath))
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ " + kv.Key + ": путь вне проекта или недопустимый" + NL);

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

                if (!content.EndsWith(lineBreak))
                    content += lineBreak;

                File.WriteAllText(outPath, content, new UTF8Encoding(false));

                WriteColored(ConsoleColor.Green,
                    "  ✔ " + kv.Key + NL);

                LogChange(outPath, "MODIFY", "success");

                written++;
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ " + kv.Key + ": " + ex.Message + NL);

                LogChange(kv.Key, "MODIFY", "error");
            }
        }

        WriteColored(ConsoleColor.Green,
            NL + "✔ План выполнен · 1 запрос · файлов: " + written + NL);
    }
}