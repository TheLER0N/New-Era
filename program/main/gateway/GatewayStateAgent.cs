using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MainApp;

internal sealed partial class GatewayState
{
    public List<ActionCard> NewCards(AgentSession s)
    {
        var list = s.Cards.Skip(s.CardsSent).ToList();
        s.CardsSent = s.Cards.Count;
        return list;
    }
    public object Finish(AgentSession s, string text, string resultStatus = "success")
    {
        // ── РАУНД 3: Автозапись краткосрочной памяти ──────────
        try
        {
            var lastUser = "";
            var lastMsg = s.Messages.LastOrDefault();
            if (lastMsg is JsonObject lastObj) {
                var role = lastObj["role"]?.ToString() ?? "";
                var txt = lastObj["content"]?.ToString() ?? "";
                if (role == "user" && !string.IsNullOrWhiteSpace(txt)) lastUser = txt;
            }
            if (!string.IsNullOrWhiteSpace(lastUser) && !string.IsNullOrWhiteSpace(text))
            {
                var userShort = lastUser.Length > 150 ? lastUser.Substring(0, 150) + "..." : lastUser;
                var aiShort = text.Length > 250 ? text.Substring(0, 250) + "..." : text;
                var memChanged = s.ChangedFiles.Take(3).ToList();
                if (memChanged.Count > 0) aiShort += " · файлы: " + string.Join(", ", memChanged);
                MemoryStore.PushShort(s.Root ?? "", userShort, aiShort);
            }
        }
        catch { }
        AgentLog($"[FINAL] {Truncate(text, 500)}");
        if (!string.IsNullOrEmpty(s.Role))
            LogRole(s.Role, $"[AGENT]: {Truncate(text, 300)}");
        var changed = s.ChangedFiles.Take(50).ToList();
        var details = new StringBuilder();
        details.AppendLine(text);
        if (changed.Count > 0)
        {
            details.AppendLine();
            details.AppendLine("Изменённые файлы:");
            foreach (var f in changed) details.AppendLine("- " + f);
        }
        if (s.ToolLog.Count > 0)
        {
            details.AppendLine();
            details.AppendLine("Действия:");
            foreach (var t in s.ToolLog.TakeLast(12)) details.AppendLine("- " + t);
        }
        s.Cards.Add(new ActionCard
        {
            Type = "summary",
            Icon = resultStatus == "failed" ? "❌" : resultStatus == "needs_user" ? "⚠️" : "✅",
            Title = "Итог", Status = resultStatus, Details = details.ToString()
        });
        return new
        {
            status = "final", role = s.Role, resultStatus, response = text,
            tools = s.ToolLog, cards = NewCards(s), changedFiles = changed,
            stepsUsed = s.StepUsed, stepLimit = s.StepLimit
        };
    }
    public object ApprovalPause(AgentSession s, PendingTool c)
    {
        var sid = Guid.NewGuid().ToString("N");
        AgentSessions[sid] = s;
        return new
        {
            status = "approval", sessionId = sid, role = s.Role,
            tool = c.Name, arguments = c.Args.ToJsonString(),
            dangerous = IsDangerousTool(s, c), cards = NewCards(s),
            stepsUsed = s.StepUsed, stepLimit = s.StepLimit
        };
    }
    // Раунд 2: arguments.questions[] — массив вопросов одной карточкой.
    // Нет questions[] — fallback на старый формат question/options.
    public static List<QuestionDto> ParseQuestions(JsonObject args)
    {
        var questions = new List<QuestionDto>();
        if (args["questions"] is JsonArray qArr)
        {
            int n = 1;
            foreach (var item in qArr)
            {
                if (item is not JsonObject qo) continue;
                var opts = new List<string>();
                if (qo["options"] is JsonArray oArr)
                {
                    foreach (var o in oArr)
                    {
                        try { var v = o?.GetValue<string>(); if (!string.IsNullOrEmpty(v)) opts.Add(v); } catch { }
                    }
                }
                questions.Add(new QuestionDto
                {
                    Id = GetStr(qo, "id", $"q{n}"),
                    Text = GetStr(qo, "text", $"Уточнение {n}"),
                    Options = opts,
                    AllowCustom = GetBool(qo, "allow_custom", true)
                });
                n++;
            }
        }
        if (questions.Count == 0)
        {
            var options = new List<string>();
            if (args["options"] is JsonArray optArr)
            {
                foreach (var item in optArr)
                {
                    try { var v = item?.GetValue<string>(); if (!string.IsNullOrEmpty(v)) options.Add(v); } catch { }
                }
            }
            questions.Add(new QuestionDto
            {
                Id = "q1",
                Text = GetStr(args, "question", "Нужна дополнительная информация."),
                Options = options,
                AllowCustom = true
            });
        }
        return questions;
    }
    public object PauseSpecial(AgentSession s, PendingTool c)
    {
        var sid = Guid.NewGuid().ToString("N");
        AgentSessions[sid] = s;
        if (c.Name == "request_more_steps")
        {
            return new
            {
                status = "more_steps", sessionId = sid, role = s.Role,
                requestedCount = GetInt(c.Args, "count", 10),
                reason = GetStr(c.Args, "reason", "Нужно больше шагов."),
                cards = NewCards(s),
                stepsUsed = s.StepUsed, stepLimit = s.StepLimit
            };
        }
        if (c.Name == "request_user_input")
        {
            var questions = ParseQuestions(c.Args);
            return new
            {
                status = "user_input", sessionId = sid, role = s.Role,
                question = questions[0].Text,
                options = questions[0].Options,
                questions,
                cards = NewCards(s),
                stepsUsed = s.StepUsed, stepLimit = s.StepLimit
            };
        }
        if (c.Name == "request_outside_access")
        {
            return new
            {
                status = "outside_access", sessionId = sid, role = s.Role,
                path = GetStr(c.Args, "path", ""),
                reason = GetStr(c.Args, "reason", ""),
                requestedActions = GetStr(c.Args, "requested_actions", "read"),
                cards = NewCards(s),
                stepsUsed = s.StepUsed, stepLimit = s.StepLimit
            };
        }
        return ApprovalPause(s, c);
    }
    public string BrowserInstruction(AgentSession s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ты русскоязычный coding agent внутри LERON GUI.");
        sb.AppendLine("Работай только внутри корня проекта, если пользователь явно не подтвердил выход за проект.");
        sb.AppendLine("Сначала читай файлы и анализируй, потом предлагай изменения.");
        sb.AppendLine("Изменения делай только через инструменты.");
        sb.AppendLine("Не выдумывай содержимое файлов, которые не читал.");
        sb.AppendLine();
        if (s.Root != null)
        {
            sb.AppendLine($"Корень проекта: {s.Root}");
            sb.AppendLine();
            sb.AppendLine("=== СТРУКТУРА ПРОЕКТА ===");
            sb.AppendLine(GetProjectTree(s.Root, 1000));
            // Раунд 3: после дерева — краткие описания файлов из .leron/file_index.json.
            var indexPrompt = GetFileIndexPrompt(s);
            if (!string.IsNullOrEmpty(indexPrompt))
            {
                sb.AppendLine();
                sb.Append(indexPrompt);
            }

            // ── РАУНД 3: Блок памяти проекта ──────────────────
            var memoryPrompt = GetMemoryPrompt(s);
            if (!string.IsNullOrEmpty(memoryPrompt))
            {
                sb.AppendLine();
                sb.Append(memoryPrompt);
            }
        }
        if (s.AllowTools)
        {
            if (s.Mode == "plan")
            {
                sb.AppendLine("Режим: планирование. Разрешены только read_file, read_files, list_files, grep, request_user_input, finish.");
sb.AppendLine();
sb.AppendLine("=== РЕЖИМ РАЗМЫШЛЕНИЯ (раунд 3) ===");
sb.AppendLine("Включай размышление (think: true) ТОЛЬКО для:");
sb.AppendLine("  - Составление плана работы");
sb.AppendLine("  - Написание кода (write_file, patch_file)");
sb.AppendLine("  - Сложные решения архитектуры");
sb.AppendLine("НЕ включай размышление (think: false) для:");
sb.AppendLine("  - Чтение файлов (read_file, read_files)");
sb.AppendLine("  - Поиск (grep, list_files)");
sb.AppendLine("  - Ответы на вопросы пользователя");
sb.AppendLine("  - Простые действия (rename_file, delete_file)");
sb.AppendLine("Это ускоряет работу — размышление только когда действительно нужно.");
            sb.AppendLine("- Читай и редактируй, когда сам считаешь нужным; НЕСКОЛЬКО инструментов в ОДНОМ ответе — цепочкой JSON-объектов подряд в одном сообщении. Это экономит запросы.");
sb.AppendLine("В режиме планирования задавай вопросы ТОЛЬКО через request_user_input — GUI покажет карточку и пользователь ответит пакетом.");
                sb.AppendLine($"ты в режиме планирования: прочитай файлы, затем задай {s.PlanRounds} раундов уточняющих вопросов. В каждом раунде ОДИН request_user_input с массивом questions из {s.PlanMin}–{s.PlanMax} вопросов формата {{\"id\":\"q1\",\"text\":\"текст вопроса\",\"options\":[\"вариант1\",\"вариант2\"],\"allow_custom\":true}} (options — 2–4 варианта; все вопросы раунда приходят одной карточкой и пользователь отвечает одним сообщением). Пример: {{\"name\":\"request_user_input\",\"arguments\":{{\"questions\":[{{\"id\":\"q1\",\"text\":\"Какой режим игры нужен?\",\"options\":[\"Локальный\",\"Сетевой\"],\"allow_custom\":true}}]}}}}. После ответов всех раундов составь нумерованный план работ и вызови finish с планом в summary.");
            }
            else
            {
                sb.AppendLine("Доступные инструменты:");
                sb.AppendLine("- read_file (один файл целиком, лимит 100000 символов; вывод с номерами строк)");
                sb.AppendLine("- read_files (без лимита файлов, общий лимит 200000 символов; невлезшие — списком, дочитай следующим запросом)");
                sb.AppendLine("- list_files");
                sb.AppendLine("- grep (массив patterns; либо findstr/shell — выбирай сам)");
sb.AppendLine("  ГАЙДЛАЙН ПОИСКА: не знаешь файл → читай через read_file; знаешь структуру → grep с массивом паттернов (все за 1 запрос); grep не нашёл → read_file для детального изучения.");
                sb.AppendLine("- write_file, write_files, patch_file, rename_file, delete_file, create_directory");
                sb.AppendLine("- run_command (сборка/тесты/компиляция и служебные команды; shell по умолчанию PowerShell; timeout_ms и max_output выбирай сам)");
            sb.AppendLine("- Читай и редактируй, когда сам считаешь нужным; НЕСКОЛЬКО инструментов в ОДНОМ ответе — цепочкой JSON-объектов подряд в одном сообщении. Это экономит запросы.");
sb.AppendLine("- update_file_summaries (пишет краткие описания файлов в .leron/file_index.json; вызывай ПОСЛЕ каждого изменения файла)");
sb.AppendLine("- file_read_exact (точное чтение строк: {path, start_line, end_line} → возвращает строки с номерами, без лимита размера)");
sb.AppendLine("- file_write_full (полная перезапись файла: {path, content})");
sb.AppendLine("- file_write_lines (замена строк по номерам: {path, start_line, end_line, content} → заменяет строки N..M на content)");
sb.AppendLine("- file_insert (вставка перед строкой: {path, line_number, content})");
sb.AppendLine("- file_append (дозапись в конец файла: {path, content})");
sb.AppendLine("  Формат индекса: { \"files\": { \"путь/к/файлу\": { \"summary\": \"1-2 предложения что делает\", \"mtime\": timestamp, \"size\": bytes } } }");
sb.AppendLine("  Вызывай update_file_summaries массивом: [{\"path\": \"path\", \"summary\": \"description\"}, ...]");
sb.AppendLine("ВАЖНО: Если файл в индексе помечен '⚠ изменён после описания' — ОБЯЗАТЕЛЬНО перечитай его через read_file ПЕРЕД любой правкой, чтобы не сломать свежий код.");
                sb.AppendLine("- request_user_input (ПАКЕТ вопросов: 5–20 вопросов одной карточкой, массив questions[] с id/type/text/options/allow_custom; пользователь отвечает одним сообщением на все сразу)");
sb.AppendLine("- request_more_steps (если не хватает шагов; варианты +10/+20/Стоп)");
sb.AppendLine("- request_outside_access, finish");
                sb.AppendLine();
                sb.AppendLine("ПРАВИЛА ФАЙЛОВ (раунд 5):");
                sb.AppendLine("- Длинные файлы (100+ строк) правь через file_read_exact → file_write_lines по номерам строк. Не цитируй текст для поиска — используй номера строк, это никогда не ломается.");
                sb.AppendLine("- Новые и короткие файлы (до 100 строк) создавай/пиши через write_file.");
                sb.AppendLine("- Длинные файлы (100+ строк) изменяй ТОЛЬКО через patch_file (замена куска строк); write_file поверх длинного файла запрещён.");
                sb.AppendLine("- Файл пустой или короче 100 строк — можно write_file целиком.");
                sb.AppendLine("- Если заметил ошибку в СВОЁМ же куске (опечатка, сломанный синтаксис, неверное имя) — ОБЯЗАН сразу добавить второй patch_file с исправлением в ТОМ ЖЕ ответе, не откладывая на следующий шаг.");
                sb.AppendLine();
                sb.AppendLine("СБОРКА И КОМАНДЫ (раунд 5):");
                sb.AppendLine("- Сборка проекта запускается ТОЛЬКО автоматически на finish — автозапуск на finish; при необходимости можешь запустить её сам через run_command.");
                sb.AppendLine("- run_command без подтверждения пользователя разрешён ТОЛЬКО для команды проверки проекта и тестов (npm test / pytest / dotnet test …); любая другая команда уйдёт на подтверждение.");
                sb.AppendLine();
                sb.AppendLine("ЭКОНОМИЯ ЗАПРОСОВ (каждый твой ответ = 1 запрос пользователя):");
                sb.AppendLine("- Файлы читай ТОЛЬКО read_file/read_files: полное содержимое в UTF-8 за один запрос. Файл до 300–400 строк читай сразу целиком, без grep и без чтения по частям.");
                sb.AppendLine("- Чтение/разведка через shell (type/cat/more/head/tail/dir/ls/tree/Get-Content/Select-Object) разрешены, но read_file/read_files предпочтительнее для экономии запросов.");
            sb.AppendLine("- Читай и редактируй, когда сам считаешь нужным; НЕСКОЛЬКО инструментов в ОДНОМ ответе — цепочкой JSON-объектов подряд в одном сообщении. Это экономит запросы.");
                sb.AppendLine("- Переносы строк внутри JSON-строк пиши как \\n, а не настоящими переносами, иначе JSON не распарсится и запрос сгорит.");
                sb.AppendLine("- Если нужно спросить пользователя — ОДИН request_user_input со всеми вопросами в массиве questions (каждый вопрос: id, text, options 2–4 варианта, allow_custom), пользователь ответит одним сообщением сразу на все.");
                sb.AppendLine("- После каждого изменения файла вызови update_file_summaries: {\"name\":\"update_file_summaries\",\"arguments\":{\"summaries\":[{\"path\":\"относительный/путь.cs\",\"summary\":\"краткое описание\"}]}}, чтобы следующий промт показывал свежее описание.");
            }
        }
        else
        {
            sb.AppendLine("Режим: обычный чат. Инструменты недоступны.");
        }
        sb.AppendLine();
        sb.AppendLine($"Текущий лимит шагов: {s.StepLimit}. Использовано: {s.StepUsed}.");
        sb.AppendLine("Каждый шаг = один запрос к ИИ. Делай запросы ёмкими: читай сразу несколько файлов через read_files, пиши несколько через write_files, ищи несколько паттернов через grep.");
        sb.AppendLine("Если шагов не хватает, вызови request_more_steps с аргументом add: 10 или 20. Пользователь увидит карточку «+10 / +20 / Стоп».");
sb.AppendLine();
sb.AppendLine("Режим размышления переключается пользователем через Ctrl+6 или кнопку в шапке чата.");
sb.AppendLine("Если пользователь включил размышление глобально — оно будет на всех шагах.");
sb.AppendLine("Если выключил — нигде не будет. Твоё решение think: true/false применяется только когда тумблер в авто-режиме.");
        sb.AppendLine("Если нужно выйти за пределы проекта, вызови request_outside_access.");
        sb.AppendLine();
        if (s.Root != null)
        {
            var check = EnsureCheckCommand(s.Root);
            if (!string.IsNullOrWhiteSpace(check))
                sb.AppendLine($"Команда проверки проекта: {check}. Она выполнится автоматически ОДИН раз на finish — автозапуск на finish; при необходимости можешь запустить её сам через run_command.");
        }
        if (s.Mode == "repair")
        {
            sb.AppendLine("Режим ремонта: чини ровно ОДНУ ошибку минимально — один маленький patch_file, без лишних правок.");
        }
        if (s.RepairMode)
        {
            sb.AppendLine("ВНИМАНИЕ: финальная проверка проекта упала. Сейчас ремонт: прочитай ошибку, найди причину (read_file/read_files), сделай минимальный patch_file и вызови finish для повторной проверки.");
        }
        sb.AppendLine();
        sb.AppendLine("ОТВЕТ-ПРОТОКОЛ: если нужно действие — ответь ОДНИМ ИЛИ НЕСКОЛЬКИМИ JSON-блоками ПОДРЯД без текста между ними:");
        sb.AppendLine("{\"name\":\"имя\",\"arguments\":{...}}{\"name\":\"имя2\",\"arguments\":{...}}");
        sb.AppendLine("Блок finish можно поставить последним — предыдущие действия выполнятся, а задача завершится без нового запроса.");
        sb.AppendLine("Действие не нужно — ответь обычным текстом на русском.");
        sb.AppendLine("Используй ТОЛЬКО инструменты из списка выше. Не выдумывай web_search, list_directory и т.п.");
        sb.AppendLine();
        return sb.ToString();
    }
    public string ToolResultPrompt(string name, string result) =>
        $"Результат выполнения {name}:\n{Truncate(result, 6000)}\n" +
        "Если задача завершена — ответь одним JSON-блоком {\"name\":\"finish\",\"arguments\":{\"summary\":\"...\"}} или обычным текстом.\n" +
        "Если нужны ещё действия — один или несколько JSON-блоков подряд.";
    // Раунд 5: проверка проекта на finish БЕЗ жёсткого лимита попыток —
    // чиним, пока хватает StepLimit; остановка только по 3 одинаковым ошибкам
    // подряд или по исчерпании шагов (карточка «продолжить чинить?»).
    public async Task<CommandResult?> RunFinishCheckAsync(AgentSession s)
    {
        if (s.Root == null || s.Mode is "chat" or "plan") return null;
        if (s.Mode != "repair" && !s.AutoRepair) return null;
        var cmd = EnsureCheckCommand(s.Root);
        if (string.IsNullOrWhiteSpace(cmd) || IsDangerousCommand(cmd)) return null;
        var res = await RunProcessAsync(cmd, s.Root, 180000);
        s.Cards.Add(new ActionCard
        {
            Type = res.ExitCode == 0 ? "command" : "repair",
            Icon = res.ExitCode == 0 ? "▶️" : "🔧",
            Title = "Проверка проекта",
            Status = res.ExitCode == 0 ? "OK" : $"exit {res.ExitCode}",
            Command = cmd, Shell = res.Shell, ExitCode = res.ExitCode,
            Details = res.Output
        });
        s.ToolLog.Add($"check \"{cmd}\" → exit {res.ExitCode}");
        AgentLog($"[CHECK] {cmd} → exit {res.ExitCode}");
        if (res.ExitCode == 0)
        {
            s.RepairMode = false;
            s.RepairAttempts = 0;
            s.SameErrorStreak = 0;
            s.LastCheckError = "";
        }
        else
        {
            s.RepairMode = true;
            s.RepairAttempts++;
        }
        return res;
    }
    public async Task<object> RunBrowserAgentLoopAsync(AgentSession s, CancellationToken loopToken)
    {
        for (int guard = 0; guard < 128; guard++)
        {
            if (loopToken.IsCancellationRequested)
                return Finish(s, "⏹ Цикл агента завершён (новая задача или отмена).", "failed");
            if (s.StepUsed >= s.StepLimit)
            {
                if (s.Pending.Count > 0 && IsSpecial(s.Pending.Peek().Name))
                    return PauseSpecial(s, s.Pending.Peek());
                // Раунд 5: в ремонте при конце шагов — сразу карточка «продолжить чинить?».
                if (s.RepairMode)
                {
                    s.Pending.Enqueue(new PendingTool
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "request_user_input",
                        Args = new JsonObject
                        {
                            ["question"] = "Шаги на ремонт исчерпаны. Продолжить чинить?",
                            ["options"] = new JsonArray("да", "нет")
                        }
                    });
                    return PauseSpecial(s, s.Pending.Peek());
                }
                var limit = new PendingTool
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "request_more_steps",
                    Args = new JsonObject
                    {
                        ["count"] = 10,
                        ["reason"] = $"Лимит шагов исчерпан ({s.StepLimit}). Задача ещё не завершена."
                    }
                };
                s.Pending.Enqueue(limit);
                return PauseSpecial(s, limit);
            }
            while (s.Pending.Count > 0)
            {
                var head = s.Pending.Peek();
                if (IsSpecial(head.Name)) return PauseSpecial(s, head);
                if (NeedsAsk(s, head)) return ApprovalPause(s, head);
                s.Pending.Dequeue();
                s.BrowserNextPrompt = await ExecuteApprovedToolAsync(s, head);
            }
            string body;
            if (!string.IsNullOrEmpty(s.BrowserNextPrompt))
            {
                body = s.BrowserNextPrompt;
                s.BrowserNextPrompt = "";
            }
            else
            {
                body = s.Messages.Count > 0
                    ? s.Messages[s.Messages.Count - 1]?["content"]?.GetValue<string>() ?? "" : "";
            }
            string task = s.Messages.Count > 0 ? s.Messages[0]?["content"]?.GetValue<string>() ?? "" : "";
            string prompt = BrowserInstruction(s) + "\nЗАДАЧА ПОЛЬЗОВАТЕЛЯ:\n" + task + "\n" + body;
            var reqId = NextReqId();
            AgentLog($"[BROWSER] шаг {s.StepUsed + 1} reqid={reqId}: {Truncate(prompt, 220)}");
            var (ok, text) = await SendToBrowserAndWait(s.Role, prompt, s.Think, Timeout.Infinite, loopToken, reqId);
            if (!ok)
            {
                AgentLog($"[BROWSER ERROR] {text}");
                if (text == "cancelled")
                    return Finish(s, "⏹ Отменено пользователем.", "failed");
                return Finish(s, s.ToolLog.Count > 0
                    ? $"⚠ {text}\nВыполнено действий: {s.ToolLog.Count}."
                    : $"⚠ {text}", "failed");
            }
            s.StepUsed++;
            var parsed = TryParseAllToolCalls(text);
            if (parsed.Count == 0)
            {
                var anyTool = TryParseAnyToolCall(text);
                if (anyTool != null && !anyTool.Value.known)
                {
                    if (s.AllowTools && s.TextRetries < 2)
                    {
                        s.TextRetries++;
                        AgentLog($"[RETRY] роль={s.Role} неизвестный инструмент '{anyTool.Value.name}' (попытка {s.TextRetries}/2)");
                        s.BrowserNextPrompt =
                            $"Неизвестный инструмент '{anyTool.Value.name}'. Его нет в системе. Разрешены только: " +
                            "read_file, read_files, list_files, grep, write_file, write_files, patch_file, rename_file, " +
                            "delete_file, create_directory, run_command, update_file_summaries, request_user_input, request_more_steps, " +
                            "request_outside_access, finish. Повтори ответ одним или несколькими корректными JSON-блоками подряд.";
                        continue;
                    }
                    if (s.AllowTools && s.Mode != "chat" && s.Mode != "plan" && s.TextRetries < 2)
{
s.TextRetries++;
AgentLog($"[TEXT-ONLY] роль={s.Role} ответ без инструментов (попытка {s.TextRetries}/2)");
s.BrowserNextPrompt = "Ты ответил обычным текстом, без вызова инструментов и без finish. План или описание не принимаются как результат. Если задача требует действий — вызови инструменты JSON-блоками подряд в одном сообщении; если работа полностью завершена — вызови {\"name\":\"finish\",\"arguments\":{\"summary\":\"...\"}}.";
continue;
}
return Finish(s, StripProviderMetadata(text), "success");
                }
                if (s.AllowTools && s.TextRetries < 2)
                {
                    s.TextRetries++;
                    AgentLog($"[RETRY] роль={s.Role} текст вместо JSON (попытка {s.TextRetries}/2)");
                    s.BrowserNextPrompt =
                        "Ты ответил текстом, но задача не завершена. Если нужно действие — ответь одним или несколькими " +
                        "JSON-блоками подряд {\"name\":\"...\",\"arguments\":{...}}. Если действий больше нет — вызови " +
                        "{\"name\":\"finish\",\"arguments\":{\"summary\":\"...\"}}.";
                    continue;
                }
                if (s.AllowTools && s.Mode != "chat" && s.Mode != "plan" && s.TextRetries < 2)
{
s.TextRetries++;
AgentLog($"[TEXT-ONLY] роль={s.Role} ответ без инструментов (попытка {s.TextRetries}/2)");
s.BrowserNextPrompt = "Ты ответил обычным текстом, без вызова инструментов и без finish. План или описание не принимаются как результат. Если задача требует действий — вызови инструменты JSON-блоками подряд в одном сообщении; если работа полностью завершена — вызови {\"name\":\"finish\",\"arguments\":{\"summary\":\"...\"}}.";
continue;
}
return Finish(s, StripProviderMetadata(text), "success");
            }
            s.TextRetries = 0;
            // Выполняем все блоки за один шаг
            string combinedResult = "";
            bool sawFinish = false;
            JsonObject? finishArgs = null;
            foreach (var (name, args) in parsed)
            {
                var c = new PendingTool
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Args = args
                };
                if (!s.AllowTools && !IsSpecial(c.Name) && c.Name != "finish")
                {
                    combinedResult += $"[Инструмент {name} недоступен в этом режиме]\n";
                    continue;
                }
                if (s.Mode == "plan" && (IsMutating(c.Name) || c.Name == "run_command"))
                {
                    combinedResult += "[режим планирования: изменения запрещены, составь план текстом и вызови finish]\n";
                    continue;
                }
                if (c.Name == "finish")
                {
                    sawFinish = true;
                    finishArgs = args;
                    break; // блоки после finish игнорируются
                }
                if (IsSpecial(c.Name))
                {
                    // Специальные запросы прерывают цепочку
                    s.Pending.Enqueue(c);
                    // Если уже есть выполненные блоки — возвращаем их результат + паузу
                    if (!string.IsNullOrEmpty(combinedResult))
                    {
                        s.BrowserNextPrompt = combinedResult;
                        return PauseSpecial(s, c);
                    }
                    return PauseSpecial(s, c);
                }
                if (NeedsAsk(s, c))
                {
                    // Требуется подтверждение — прерываем цепочку
                    s.Pending.Enqueue(c);
                    if (!string.IsNullOrEmpty(combinedResult))
                    {
                        s.BrowserNextPrompt = combinedResult;
                        return ApprovalPause(s, c);
                    }
                    return ApprovalPause(s, c);
                }
                var result = await ExecuteApprovedToolAsync(s, c);
                combinedResult += result + "\n";
            }
            if (sawFinish)
            {
                // Завершаем задачу без нового запроса
                var summary = finishArgs != null ? GetStr(finishArgs, "summary", "Задача завершена.") : "Задача завершена.";
                var status = finishArgs != null ? GetStr(finishArgs, "status", "success") : "success";
                // Раунд 5: финальная проверка без лимита попыток.
                if (s.Root != null && s.AutoRepair && s.ChangedFiles.Count > 0)
                {
                    var check = await RunFinishCheckAsync(s);
                    if (check != null)
                    {
                        if (check.ExitCode == 0)
                        {
                            combinedResult += "\nАвтоматическая проверка проекта прошла успешно.";
                        }
                        else
                        {
                            var errorText = Tail(string.IsNullOrWhiteSpace(check.StdErr) ? check.StdOut : check.StdErr, 4000);
                            s.SameErrorStreak = string.Equals(errorText, s.LastCheckError, StringComparison.Ordinal)
                                ? s.SameErrorStreak + 1 : 1;
                            s.LastCheckError = errorText;
                            bool stepsLeft = s.StepUsed < s.StepLimit;
                            // Чиним, пока хватает шагов и ошибки не повторяются 3 раза подряд.
                            if (stepsLeft && s.SameErrorStreak < 3)
                            {
                                s.BrowserNextPrompt =
                                    $"Автоматическая проверка упала (попытка {s.RepairAttempts}).\nОшибка проверки:\n{errorText}\n" +
                                    "Режим ремонта: прочитай ошибку, найди причину (read_file/read_files), сделай минимальный patch_file " +
                                    "и вызови finish для повторной проверки. Если заметил ошибку в своём куске — сразу добавь второй patch_file в том же ответе.";
                                continue;
                            }
                            combinedResult += "\nОшибка проверки:\n" + errorText;
                            // 3 одинаковых ошибки подряд или шаги исчерпаны — карточка да/нет.
                            s.Pending.Enqueue(new PendingTool
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = "request_user_input",
                                Args = new JsonObject
                                {
                                    ["question"] = stepsLeft
                                        ? $"Одна и та же ошибка проверки повторяется {s.SameErrorStreak} раза подряд. Продолжить чинить?"
                                        : "Шаги на ремонт исчерпаны. Продолжить чинить?",
                                    ["options"] = new JsonArray("да", "нет")
                                }
                            });
                            return PauseSpecial(s, s.Pending.Peek());
                        }
                    }
                }
                return Finish(s, summary + (string.IsNullOrEmpty(combinedResult) ? "" : "\n" + combinedResult), status);
            }
            if (!string.IsNullOrEmpty(combinedResult))
            {
                s.BrowserNextPrompt = combinedResult;
            }
        }
        return Finish(s, "Агент остановлен по внутреннему лимиту.", "failed");
    }
    public async Task<(bool ok, string text)> SendToBrowserAndWait(
        string role, string text, bool think, int timeoutMs, CancellationToken abort, string reqId)
    {
        var sem = RoleSendLocks.GetOrAdd(role, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(TimeSpan.FromSeconds(30), abort))
return (false, "Шлюз занят предыдущим запросом. Повтори через несколько секунд.");
        try
        {
            if (abort.IsCancellationRequested) return (false, "cancelled");
TryAutoBind(role);
            if (!RoleChatMap.ContainsKey(role))
                return (false, $"Роль '{role}' не закреплена за чатом. Закрепи роль в Qwen.");
            ExpectedReqId[role] = reqId;
            LastSentText[role] = text;
            AgentLog($"[SEND] роль={role} reqid={reqId} текст={Truncate(text, 120)}");
            LogRole(role, $"[USER]: {Truncate(text, 300)}");
            if (OrphanResponses.TryRemove(role, out var early) &&
                (string.IsNullOrEmpty(early.reqId) || early.reqId == reqId))
            {
                AgentLog($"[ORPHAN] роль={role} ответ подхвачен из буфера до отправки");
                LastSentText.TryRemove(role, out _);
                return (true, early.text);
            }
            var tcs = new TaskCompletionSource<string>();
            PendingResponses[role] = tcs;
            var cts = new CancellationTokenSource();
            PendingCancels[role] = cts;
            using var abortReg = abort.Register(() => tcs.TrySetCanceled());
            if (OrphanResponses.TryRemove(role, out var between) &&
                (string.IsNullOrEmpty(between.reqId) || between.reqId == reqId))
            {
                PendingResponses.TryRemove(role, out _);
                PendingCancels.TryRemove(role, out _);
                LastSentText.TryRemove(role, out _);
                AgentLog($"[ORPHAN] роль={role} ответ подхвачен из буфера до ожидания");
                return (true, between.text);
            }
            var chatId = RoleChatMap[role];
            var url = Config.Roles.TryGetValue(role, out var roleCfg) && !string.IsNullOrEmpty(roleCfg.Url)
                ? roleCfg.Url : $"https://chat.qwen.ai/c/{chatId}";
            var payload = Encoding.UTF8.GetBytes(
                $"TYPE:{role}|{chatId}|{url}|{(think ? "1" : "0")}|{reqId}|{text}");
            LastTypePayload[role] = payload;
            bool sent = false;
            foreach (var client in Clients)
            {
                if (client.State != WebSocketState.Open) continue;
try
{
await client.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
sent = true;
break;
}
catch { Clients.Remove(client); }
            }
            if (!sent)
            {
                PendingResponses.TryRemove(role, out _);
                PendingCancels.TryRemove(role, out _);
                return (false, "Браузерная панель не подключена. Открой LERON GUI и дождись подключения.");
            }
            var cancelTask = Task.Run(async () => { try { await Task.Delay(Timeout.Infinite, cts.Token); } catch { } });
            Task? delayTask = timeoutMs > 0 ? Task.Delay(timeoutMs) : null;
            while (true)
            {
                var racers = new List<Task> { tcs.Task, cancelTask };
                racers.Add(delayTask ?? Task.Delay(2000));
                var done = await Task.WhenAny(racers);
                if (done == tcs.Task || done == cancelTask) break;
                if (delayTask != null) break;
                if (abort.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                    break;
                }
                if (OrphanResponses.TryGetValue(role, out var orph) &&
                    (string.IsNullOrEmpty(orph.reqId) || orph.reqId == reqId))
                {
                    OrphanResponses.TryRemove(role, out _);
                    AgentLog($"[ORPHAN] роль={role} ответ подхвачен из буфера во время ожидания");
                    tcs.TrySetResult(orph.text);
                    break;
                }
            }
            PendingResponses.TryRemove(role, out _);
            PendingCancels.TryRemove(role, out _);
            LastSentText.TryRemove(role, out _);
            if (tcs.Task.IsCompleted)
            {
                if (tcs.Task.IsCanceled || abort.IsCancellationRequested) return (false, "cancelled");
                return (true, await tcs.Task);
            }
            if (delayTask != null)
                return (false, "Браузер не ответил в отведённое время.");
            return (false, "cancelled");
        }
        catch (OperationCanceledException)
        {
            PendingResponses.TryRemove(role, out _);
            PendingCancels.TryRemove(role, out _);
            LastSentText.TryRemove(role, out _);
            return (false, "cancelled");
        }
finally
{
sem.Release();
}
}

    public bool NeedsAsk(AgentSession s, PendingTool c)
    {
        if (c.Name == "finish") return false;
        if (IsSpecial(c.Name)) return false;
        if (s.Mode == "yolo") return false;
        if (IsDangerousTool(s, c)) return true;
        string rule;
        if (c.Name == "run_command")
        {
            var cmd = GetStr(c.Args, "command");
            if (IsTestCommand(cmd)) return false;
            rule = CommandKey(cmd);
        }
        else
        {
            var raw = GetStr(c.Args, "path");
            var resolved = ResolveSessionPath(s, raw, "write");
            rule = PathRule(c.Name, resolved ?? raw, s.Root);
        }
        if (s.Mode == "auto") return !IsAutoApproved(rule);
        return true;
    }

    public async Task<object> HandleApproveAsync(AgentSession s, ApproveRequest req, CancellationToken abort)
    {
        if (req.Steps > 0)
        {
            s.StepLimit += req.Steps;
            AgentLog($"[STEPS] +{req.Steps} → лимит {s.StepLimit}");
        }
        if (s.Pending.Count == 0)
            return Finish(s, "Нет ожидающих действий.", "failed");
        var head = s.Pending.Peek();
        if (!req.Approve)
        {
            s.Pending.Dequeue();
            if (head.Name == "request_outside_access" || IsDangerousTool(s, head))
                return Finish(s, "Действие отклонено пользователем.", "failed");
            s.BrowserNextPrompt = $"Действие {head.Name} отклонено пользователем. Предложи альтернативу или вызови finish.";
        }
        else
        {
            if (req.Remember && !IsSpecial(head.Name))
            {
                string rule;
                if (head.Name == "run_command")
                    rule = CommandKey(GetStr(head.Args, "command"));
                else
                {
                    var raw = GetStr(head.Args, "path");
                    var resolved = ResolveSessionPath(s, raw, "write");
                    rule = PathRule(head.Name, resolved ?? raw, s.Root);
                }
                AddAutoRule(rule);
            }
            s.Pending.Dequeue();
            if (head.Name == "request_outside_access")
            {
                var path = GetStr(head.Args, "path");
                var actions = GetStr(head.Args, "requested_actions", "read");
                var grant = new OutsideGrant { Path = path, Actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
                foreach (var a in actions.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    grant.Actions.Add(a.Trim());
                s.OutsideGrants.Add(grant);
                s.BrowserNextPrompt = "Доступ разрешён пользователем. Продолжай.";
            }
            else if (head.Name == "request_user_input")
            {
                s.BrowserNextPrompt = !string.IsNullOrEmpty(req.InputText)
                    ? $"Ответ пользователя: {req.InputText}"
                    : "Пользователь подтвердил без текста. Продолжай.";
            }
            else if (head.Name == "request_more_steps")
            {
                s.BrowserNextPrompt = "Шаги добавлены. Продолжай.";
            }
            else
            {
                s.BrowserNextPrompt = await ExecuteApprovedToolAsync(s, head);
            }
        }
        var loopCts = BeginLoop(s.Role, abort);
        var result = await RunBrowserAgentLoopAsync(s, loopCts.Token);
        EndLoop(s.Role, loopCts);
        return result;
    }
}
        // === INSTRUCTION UPDATE ===
        // Add to BrowserInstruction: "Длинные файлы правь через file_read_exact -> file_write_lines по номерам строк, не цитируй текст."
