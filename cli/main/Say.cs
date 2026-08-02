// Say.cs — отправка сообщения в ИИ (v6.0)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  SAY
    // ══════════════════════════════════════════════════════════
    static void Say(string text)
    {
        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId))
        {
            WriteColored(ConsoleColor.Red,
                "  ✖ Нет конфигурации. Заполни qwen_config.txt (CHAT_ID, TOKEN).\n");
            WriteColored(ConsoleColor.DarkGray,
                "    " + ConfigFile + "\n");
            return;
        }

        AddHistory("user", text);

        string finalPrompt = text;
        string projectPath = ResolveProjectPath();

        if (DispatcherEnabled)
        {
            WriteColored(ConsoleColor.Magenta, "  ◆ dispatcher\n");

            StartSpinner("диспетчер");

            try
            {
                DispatchResult dispatch = DispatchRequest(text, projectPath);
                string built = BuildPrimaryPrompt(dispatch, projectPath);

                if (!string.IsNullOrWhiteSpace(built))
                    finalPrompt = built;
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Yellow,
                    "  ⚠ dispatcher: " + ex.Message + " — bypass\n");

                finalPrompt = text;
            }

            StopSpinner();
        }

        StartSpinner("отправка");

        string responseText = null;

        try
        {
            string raw = PostMessage(finalPrompt, LastResponseId);

            try
            {
                File.WriteAllText(DumpFile, raw ?? "", new UTF8Encoding(false));
            }
            catch { }

            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();

            string msg = ex.Message;

            if (msg.Contains("401") || msg.Contains("403"))
                WriteColored(ConsoleColor.Red,
                    "  ✖ Токен истёк. Обнови qwen_config.txt.\n");
            else if (msg.Contains("429"))
                WriteColored(ConsoleColor.Yellow,
                    "  ⚠ Слишком много запросов. Подожди 30 сек.\n");
            else
                WriteColored(ConsoleColor.Red,
                    "  ✖ Ошибка: " + msg + "\n");

            return;
        }

        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ Пустой ответ. Попробуй ещё раз.\n");
            return;
        }

        if (DispatcherEnabled)
        {
            CodeWriterResult fileResult = ExtractCodeOrLocal(responseText);

            if (fileResult != null && !fileResult.IsEmpty)
                ApplyValidatedFiles(fileResult, projectPath, false);
        }

        AddHistory("assistant", responseText);
        RenderAssistantMessage(responseText);
    }

    // ══════════════════════════════════════════════════════════
    //  LOCAL FALLBACK HELPERS
    // ══════════════════════════════════════════════════════════
    static bool LooksLikeCodeWriterMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        bool hasFile = text.IndexOf("FILE:", StringComparison.OrdinalIgnoreCase) >= 0;
        bool hasEnd = text.IndexOf("END_FILE", StringComparison.OrdinalIgnoreCase) >= 0;

        return hasFile && hasEnd;
    }

    static bool LooksLikeLegacyFileBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.IndexOf("=== FILE:", StringComparison.OrdinalIgnoreCase) >= 0
            && text.IndexOf("=== END ===", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static CodeWriterResult ConvertLegacyFileBlocks(Dictionary<string, string> blocks)
    {
        var result = new CodeWriterResult();

        if (blocks == null || blocks.Count == 0)
            return result;

        foreach (var kv in blocks)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
                continue;

            var op = new FileOperation();

            op.Path = kv.Key.Replace('\\', '/');
            op.Action = "MODIFY";
            op.Content = kv.Value;

            result.Operations.Add(op);

            if (!result.FilesAffected.Contains(op.Path))
                result.FilesAffected.Add(op.Path);
        }

        result.HasValidMarkers = result.Operations.Count > 0;
        result.PlanConfirmed = result.Operations.Count > 0;

        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  APPLY FILES
    // ══════════════════════════════════════════════════════════
    static bool ApplyGeneratedFiles(CodeWriterResult result, string baseDir)
    {
        return ApplyGeneratedFiles(result, baseDir, false);
    }

    static bool ApplyGeneratedFiles(CodeWriterResult result, string baseDir, bool autoConfirm)
    {
        if (result == null || result.IsEmpty)
            return false;

        Console.WriteLine();

        foreach (var op in result.Operations)
        {
            WriteColored(ConsoleColor.Cyan,
                "  ▸ " + (op.Action ?? "MODIFY") + " " + (op.Path ?? "?") + "\n");
        }

        bool doWrite;

        if (autoConfirm || ArcMode)
        {
            WriteColored(ConsoleColor.Green,
                "  ✔ Авто-применение (аркест)\n");
            doWrite = true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  ❓ Применить файлы? [y/N] ");
            Console.ResetColor();

            string confirm = Console.ReadLine();
            doWrite = confirm != null && confirm.Trim().ToLowerInvariant() == "y";
        }

        if (!doWrite)
        {
            WriteColored(ConsoleColor.DarkGray, "  ◂ Отменено.\n");
            return false;
        }

        if (string.IsNullOrEmpty(baseDir))
            baseDir = BaseDir;

        int written = 0;

        foreach (var op in result.Operations)
        {
            if (string.IsNullOrWhiteSpace(op.Path))
            {
                WriteColored(ConsoleColor.Red, "  ✖ Пропущен файл без пути\n");
                continue;
            }

            string outPath;

            if (!TryResolveSafeOutputPath(baseDir, op.Path, out outPath))
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ " + op.Path + ": путь вне проекта или недопустимый\n");
                LogChange(op.Path, op.Action ?? "MODIFY", "error");
                continue;
            }

            try
            {
                SaveRollbackSnapshot(outPath);

                if (op.IsDelete)
                {
                    if (File.Exists(outPath))
                        File.Delete(outPath);

                    WriteColored(ConsoleColor.Red, "  ✖ DELETE " + outPath + "\n");
                }
                else
                {
                    string dir = Path.GetDirectoryName(outPath);

                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    string content = op.Content ?? "";

                    if (!content.EndsWith("\n"))
                        content += "\n";

                    File.WriteAllText(outPath, content, new UTF8Encoding(false));

                    WriteColored(ConsoleColor.Green, "  ✔ " + outPath + "\n");
                }

                LogChange(outPath, op.Action ?? "MODIFY", "success");
                written++;
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Red,
                    "  ✖ " + outPath + ": " + ex.Message + "\n");

                LogChange(outPath, op.Action ?? "MODIFY", "error");
            }
        }

        WriteColored(ConsoleColor.Green, "\n✔ Записано файлов: " + written + "\n");

        return written > 0;
    }
}