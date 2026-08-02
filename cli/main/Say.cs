// Say.cs — отправка сообщения в ИИ (Dispatcher + Dual-LLM + Guardian aware)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.IO;

partial class MainConsole
{
    static void Say(string text)
    {
        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(ChatId))
        {
            WriteColored(ConsoleColor.Red, "  ✖ Нет конфигурации. Заполни qwen_config.txt (CHAT_ID, TOKEN).\n");
            WriteColored(ConsoleColor.DarkGray, "    " + ConfigFile + "\n");
            return;
        }

        AddHistory("user", text);

        // ── v6.0: Dispatcher ──
        string finalPrompt = text;
        if (DispatcherEnabled)
        {
            WriteColored(ConsoleColor.Magenta, "  ◆ dispatcher\n");
            StartSpinner("диспетчер");
            try
            {
                DispatchResult dispatch = DispatchRequest(text, null);
                finalPrompt = BuildPrimaryPrompt(dispatch, null);
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Yellow, "  ⚠ dispatcher: " + ex.Message + " — bypass\n");
            }
            StopSpinner();
        }
        else if (OrchestratorEnabled)
        {
            // Legacy v4.3/v5.3
            StartSpinner("оркестрация");
            try
            {
                string orchestrated = OrchestrateRequest(text, null);
                if (!string.IsNullOrWhiteSpace(orchestrated))
                    finalPrompt = orchestrated;
            }
            catch { }
            StopSpinner();
        }

        // ── Guardian: индикация ──
        if (GuardianEnabled)
        {
            WriteColored(ConsoleColor.Magenta, "  ◆ guardian: активен");
            if (ArcMode) WriteColored(ConsoleColor.Magenta, " · аркест");
            WriteColored(ConsoleColor.Magenta, "\n");
        }

        StartSpinner("отправка");
        string responseText = null;
        try
        {
            string raw = PostMessage(finalPrompt, LastResponseId);
            try { File.WriteAllText(DumpFile, raw ?? "", new System.Text.UTF8Encoding(false)); } catch { }
            responseText = ParseSseAnswer(raw);
        }
        catch (Exception ex)
        {
            StopSpinner();
            string msg = ex.Message;
            if (msg.Contains("401") || msg.Contains("403"))
                WriteColored(ConsoleColor.Red, "  ✖ Токен истёк. Обнови qwen_config.txt.\n");
            else if (msg.Contains("429"))
                WriteColored(ConsoleColor.Yellow, "  ⚠ Слишком много запросов. Подожди 30 сек.\n");
            else
                WriteColored(ConsoleColor.Red, "  ✖ Ошибка: " + msg + "\n");
            return;
        }
        StopSpinner();

        if (string.IsNullOrWhiteSpace(responseText))
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ Пустой ответ. Попробуй ещё раз.\n");
            return;
        }

        // ── v6.0: Извлечение кода через AI #2 ──
        if (DispatcherEnabled && ExtractEnabled)
        {
            StartSpinner("экстрактор");
            try
            {
                CodeWriterResult extracted = ExtractCodeViaAI2(responseText);
                if (extracted != null && !extracted.IsEmpty)
                {
                    StopSpinner();
                    WriteColored(ConsoleColor.Green, "  ✔ dispatcher: извлечено файлов: " + extracted.Operations.Count + "\n");
                    foreach (var op in extracted.Operations)
                        WriteColored(ConsoleColor.Cyan, "    ▸ " + (op.Action ?? "?") + " " + (op.Path ?? "?") + "\n");
                }
                else
                {
                    StopSpinner();
                }
            }
            catch (Exception ex)
            {
                StopSpinner();
                WriteColored(ConsoleColor.Yellow, "  ⚠ extractor: " + ex.Message + "\n");
            }
        }

        AddHistory("assistant", responseText);
        RenderAssistantMessage(responseText);
    }
}