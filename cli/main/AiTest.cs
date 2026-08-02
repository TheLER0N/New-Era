// AiTest.cs — тестирование доступных ИИ
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// В проекте ровно ДВА ИИ:
//   [1] Primary · qwen3.8-max-preview · генератор кода
//   [2] AI #2   · qwen3.7-max          · помощник-диспетчер
//
// AI #2 один выполняет все роли:
//   enhance / select / compress / extract / validate
using System;
using System.Collections.Generic;

partial class MainConsole
{
    class AiTestTarget
    {
        public int Number;
        public string Name;
        public string Model;
        public string ApiUrl;
        public string Token;
        public string ChatId;
        public bool Configured;
        public string Note;
        public string RoleKind;
        public string SystemPrompt;
        public string QuickMessage;
    }

    // ══════════════════════════════════════════════════════════
    //  /test entry point
    // ══════════════════════════════════════════════════════════
    static void HandleTest(string input)
    {
        string args = input.Length > 5 ? input.Substring(5).Trim() : "";

        if (string.IsNullOrEmpty(args))
        {
            WriteColored(ConsoleColor.DarkGray,
                "  ◌ Введи тестовый текст (пустая строка — быстрый тест):\n");

            string text = ReadMultiline();

            if (string.IsNullOrWhiteSpace(text))
                RunTestQuick(0);
            else
                RunTestCustom(text, 0);

            return;
        }

        string lower = args.ToLowerInvariant();

        if (lower == "list" || lower == "ls")
        {
            PrintTestList();
            return;
        }

        if (lower == "quick" || lower == "fast" || lower == "auto")
        {
            RunTestQuick(0);
            return;
        }

        string[] parts = args.Split(new[] { ' ' }, 2);
        string numRaw = parts[0].Trim().Trim('<', '>', '[', ']', '(', ')');

        int num;

        if (int.TryParse(numRaw, out num))
        {
            string rest = parts.Length > 1 ? parts[1].Trim() : "";

            if (string.IsNullOrEmpty(rest))
            {
                RunTestQuick(num);
                return;
            }

            string restLower = rest.ToLowerInvariant();

            if (restLower == "quick" || restLower == "fast" || restLower == "auto")
            {
                RunTestQuick(num);
                return;
            }

            RunTestCustom(rest, num);
            return;
        }

        RunTestCustom(args, 0);
    }

    // ══════════════════════════════════════════════════════════
    //  Список ИИ (ровно 2)
    // ══════════════════════════════════════════════════════════
    static void PrintTestList()
    {
        List<AiTestTarget> targets = BuildAiTestList();

        WriteColored(ConsoleColor.DarkGray, "\n── Список ИИ для теста ──\n");

        foreach (var t in targets)
        {
            if (t.Configured)
                WriteColored(ConsoleColor.Green, "  [" + t.Number + "] " + t.Name);
            else
                WriteColored(ConsoleColor.DarkGray, "  [" + t.Number + "] " + t.Name);

            WriteColored(ConsoleColor.DarkGray, " · " + (t.Model ?? "?"));

            if (!string.IsNullOrEmpty(t.Note))
                WriteColored(ConsoleColor.DarkGray, " · " + t.Note);

            if (!t.Configured)
                WriteColored(ConsoleColor.Yellow, " · не сконфигурирован");

            Console.WriteLine();
        }

        WriteColored(ConsoleColor.DarkGray,
            "\nРоли AI #2 (один чат, последовательно): enhance · select · compress · extract · validate\n");

        WriteColored(ConsoleColor.DarkGray, "\nИспользование:\n");
        WriteColored(ConsoleColor.DarkGray, "    /test <текст>\n");
        WriteColored(ConsoleColor.DarkGray, "    /test quick\n");
        WriteColored(ConsoleColor.DarkGray, "    /test <номер> <текст>\n");
        WriteColored(ConsoleColor.DarkGray, "    /test <номер> quick\n");
    }

    static List<AiTestTarget> BuildAiTestList()
    {
        var list = new List<AiTestTarget>();
        int num = 1;

        string ai2Token = GetAi2Token();
        string ai2Api = GetAi2Api();
        string ai2Model = GetAi2Model();
        bool ai2Configured = IsAi2Configured();

        string ai2Note;

        if (ai2Configured)
            ai2Note = "помощник (enhance/select/extract/compress)";
        else if (string.IsNullOrEmpty(Token2))
            ai2Note = "нет AI2_TOKEN";
        else
            ai2Note = "нет AI2_CHAT_ID";

        // [1] Primary — генератор кода.
        list.Add(new AiTestTarget
        {
            Number = num++,
            Name = "Primary",
            Model = PrimaryModel,
            ApiUrl = ApiBaseUrl,
            Token = Token,
            ChatId = ChatId,
            Configured = !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId),
            Note = "генератор · qwen3.8-max-preview",
            RoleKind = "primary",
            SystemPrompt = null,
            QuickMessage = "Скажи привет."
        });

        // [2] AI #2 — один второй ИИ во всех ролях диспетчера.
        list.Add(new AiTestTarget
        {
            Number = num++,
            Name = "AI #2",
            Model = ai2Model,
            ApiUrl = ai2Api,
            Token = ai2Token,
            ChatId = ChatId2,
            Configured = ai2Configured,
            Note = ai2Note,
            RoleKind = "ai2",
            SystemPrompt = null,
            QuickMessage = "Скажи привет."
        });

        return list;
    }

    // ══════════════════════════════════════════════════════════
    //  Запуск тестов
    // ══════════════════════════════════════════════════════════
    static void RunTestQuick(int onlyNumber)
    {
        RunTestWithMessage(null, onlyNumber, true);
    }

    static void RunTestCustom(string text, int onlyNumber)
    {
        RunTestWithMessage(text, onlyNumber, false);
    }

    static void RunTestWithMessage(string text, int onlyNumber, bool quick)
    {
        List<AiTestTarget> targets = BuildAiTestList();
        bool found = false;

        foreach (var t in targets)
        {
            if (onlyNumber != 0 && t.Number != onlyNumber)
                continue;

            found = true;

            string message;

            if (quick)
            {
                message = !string.IsNullOrEmpty(t.QuickMessage)
                    ? t.QuickMessage
                    : "Скажи привет ии под номером " + t.Number + ".";
            }
            else
            {
                message = text;
            }

            SendTest(t, message);

            if (onlyNumber != 0)
                break;
        }

        if (!found)
        {
            if (onlyNumber != 0)
                WriteColored(ConsoleColor.Red,
                    "  ✖ ИИ с номером " + onlyNumber + " не найден. Смотри /test list\n");
            else
                WriteColored(ConsoleColor.Red,
                    "  ✖ Нет доступных ИИ для теста.\n");
        }
    }

    static void SendTest(AiTestTarget t, string message)
    {
        WriteColored(ConsoleColor.Cyan,
            "\n▸ Тест #" + t.Number + " · " + t.Name + " · " + (t.Model ?? "?") + "\n");

        if (!t.Configured)
        {
            WriteColored(ConsoleColor.Yellow,
                "  ⚠ пропуск: нет токена или chat_id\n");
            return;
        }

        StartSpinner("тест " + t.Name);

        try
        {
            string response = PostRoleChatMessage(
                t.Name,
                t.SystemPrompt,
                message,
                t.Model,
                t.ApiUrl,
                t.Token,
                t.ChatId,
                PrimaryTimeoutMs,
                PrimaryReadWriteTimeoutMs
            );

            StopSpinner();

            if (string.IsNullOrWhiteSpace(response))
            {
                WriteColored(ConsoleColor.Yellow,
                    "  ⚠ Пустой ответ.\n");
                return;
            }

            RenderAssistantMessage(response);
        }
        catch (Exception ex)
        {
            StopSpinner();

            WriteColored(ConsoleColor.Red,
                "  ✖ Ошибка: " + ex.Message + "\n");
        }
    }
}