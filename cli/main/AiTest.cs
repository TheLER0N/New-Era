// AiTest.cs — тестирование доступных ИИ / ролей
// New Era CLI v5.3 · partial class MainConsole
// C# 5 / .NET Framework 4.x

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
    }

    // ══════════════════════════════════════════════════════════
    //  /test entry point
    // ══════════════════════════════════════════════════════════
    static void HandleTest(string input)
    {
        // input приходит без ведущего slash, например: "test quick"
        string args = input.Length > 5 ? input.Substring(5).Trim() : "";

        if (string.IsNullOrEmpty(args))
        {
            WriteColored(ConsoleColor.DarkGray, "  ◌ Введи тестовый текст (пустая строка — быстрый тест):\n");
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
        int num;

        if (int.TryParse(parts[0], out num))
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
    //  Список ИИ
    // ══════════════════════════════════════════════════════════
    static void PrintTestList()
    {
        List<AiTestTarget> targets = BuildAiTestList();

        WriteColored(ConsoleColor.DarkGray, "\n  ── Список ИИ для теста ──\n");

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

        WriteColored(ConsoleColor.DarkGray, "\n  Использование:\n");
        WriteColored(ConsoleColor.DarkGray, "    /test <текст>\n");
        WriteColored(ConsoleColor.DarkGray, "    /test quick\n");
        WriteColored(ConsoleColor.DarkGray, "    /test <номер> <текст>\n");
        WriteColored(ConsoleColor.DarkGray, "    /test <номер> quick\n\n");
    }

    static List<AiTestTarget> BuildAiTestList()
    {
        var list = new List<AiTestTarget>();
        int num = 1;

        // 1. Primary
        list.Add(new AiTestTarget
        {
            Number = num++,
            Name = "Primary",
            Model = PrimaryModel,
            ApiUrl = ApiBaseUrl,
            Token = Token,
            ChatId = ChatId,
            Configured = !string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId),
            Note = "основной ИИ"
        });

        // 2. AI #2 (если задан)
        if (!string.IsNullOrEmpty(Token2))
        {
            string api2 = (string.IsNullOrEmpty(ApiBaseUrl2) || ApiBaseUrl2 == DefaultApiBase)
                ? ApiBaseUrl
                : ApiBaseUrl2;

            string chat2 = string.IsNullOrEmpty(ChatId2) ? ChatId : ChatId2;

            list.Add(new AiTestTarget
            {
                Number = num++,
                Name = "AI #2",
                Model = string.IsNullOrEmpty(Ai2Model) ? PrimaryModel : Ai2Model,
                ApiUrl = api2,
                Token = Token2,
                ChatId = chat2,
                Configured = !string.IsNullOrEmpty(Token2) && !string.IsNullOrEmpty(chat2),
                Note = "второй ИИ"
            });
        }

        // 3. Orchestrator
        string orchModel = string.IsNullOrEmpty(OrchestratorModel) ? PrimaryModel : OrchestratorModel;
        string orchApi   = string.IsNullOrEmpty(OrchestratorApiUrl) ? ApiBaseUrl : OrchestratorApiUrl;
        string orchToken = string.IsNullOrEmpty(OrchestratorToken) ? Token : OrchestratorToken;
        string orchChat  = string.IsNullOrEmpty(OrchestratorChatId) ? ChatId : OrchestratorChatId;

        string orchNote = OrchestratorEnabled ? "dual-LLM включён" : "dual-LLM выключен";

        if (orchToken == Token2 && !string.IsNullOrEmpty(Token2))
            orchNote += " · использует AI #2";

        list.Add(new AiTestTarget
        {
            Number = num++,
            Name = "Orchestrator",
            Model = orchModel,
            ApiUrl = orchApi,
            Token = orchToken,
            ChatId = orchChat,
            Configured = !string.IsNullOrEmpty(orchToken) && !string.IsNullOrEmpty(orchChat),
            Note = orchNote
        });

        // 4. Guardian
        string guardModel = string.IsNullOrEmpty(GuardianModel) ? PrimaryModel : GuardianModel;
        string guardApi   = string.IsNullOrEmpty(GuardianApiUrl) ? ApiBaseUrl : GuardianApiUrl;
        string guardToken = string.IsNullOrEmpty(GuardianToken) ? Token : GuardianToken;

        string guardChat = ChatId;
        if (guardToken == Token2 && !string.IsNullOrEmpty(ChatId2))
            guardChat = ChatId2;

        string guardNote = GuardianEnabled ? "guardian включён" : "guardian выключен";
        if (guardToken == Token2 && !string.IsNullOrEmpty(Token2))
            guardNote += " · использует AI #2";

        list.Add(new AiTestTarget
        {
            Number = num++,
            Name = "Guardian",
            Model = guardModel,
            ApiUrl = guardApi,
            Token = guardToken,
            ChatId = guardChat,
            Configured = !string.IsNullOrEmpty(guardToken) && !string.IsNullOrEmpty(guardChat),
            Note = guardNote
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

            string message = quick
                ? "Скажи привет ии под номером " + t.Number + "."
                : text;

            SendTest(t, message);

            if (onlyNumber != 0)
                break;
        }

        if (!found)
        {
            if (onlyNumber != 0)
                WriteColored(ConsoleColor.Red, "  ✖ ИИ с номером " + onlyNumber + " не найден. Смотри /test list\n");
            else
                WriteColored(ConsoleColor.Red, "  ✖ Нет доступных ИИ для теста.\n");
        }
    }

    static void SendTest(AiTestTarget t, string message)
    {
        WriteColored(ConsoleColor.Cyan, "\n ▸ Тест #" + t.Number + " · " + t.Name + " · " + (t.Model ?? "?") + "\n");

        if (!t.Configured)
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ пропуск: нет токена или chat_id\n");
            return;
        }

        StartSpinner("тест " + t.Name);

        try
        {
            string response = PostRoleChatMessage(
                t.Name,
                null,
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
                WriteColored(ConsoleColor.Yellow, "  ⚠ Пустой ответ.\n");
                return;
            }

            RenderAssistantMessage(response);
        }
        catch (Exception ex)
        {
            StopSpinner();
            WriteColored(ConsoleColor.Red, "  ✖ Ошибка: " + ex.Message + "\n");
        }
    }
}