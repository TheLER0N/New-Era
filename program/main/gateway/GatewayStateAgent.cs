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
            sb.AppendLine(GetProjectTree(s.Root, 300));
            // Раунд 3: после дерева — краткие описания файлов из .leron/file_index.json.
            var indexPrompt = GetFileIndexPrompt(s);
            if (!string.IsNullOrEmpty(indexPrompt))
            {
                sb.AppendLine();
                sb.Append(indexPrompt);
            }
        }
        if (s.AllowTools)
        {
            if (s.Mode == "plan")
            {
                sb.AppendLine("Режим: планирование. Разрешены только read_file, read_files, list_files, grep, request_user_input, finish.");
                sb.AppendLine($"ты в режиме планирования: прочитай файлы, затем задай {s.PlanRounds} раундов уточняющих вопросов. В каждом раунде ОДИН request_user_input с массивом questions из {s.PlanMin}–{s.PlanMax} вопросов формата {{\"id\":\"q1\",\"text\":\"текст вопроса\",\"options\":[\"вариант1\",\"вариант2\"],\"allow_custom\":true}} (options — 2–4 варианта; все вопросы раунда приходят одной карточкой и пользователь отвечает одним сообщением). Пример: {{\"name\":\"request_user_input\",\"arguments\":{{\"questions\":[{{\"id\":\"q1\",\"text\":\"Какой режим игры нужен?\",\"options\":[\"Локальный\",\"Сетевой\"],\"allow_custom\":true}}]}}}}. После ответов всех раундов составь нумерованный план работ и вызови finish с планом в summary.");
            }
            else
            {
                sb.AppendLine("Доступные инструменты:");
                sb.AppendLine("- read_file (один файл целиком, лимит 20000 символов)");
                sb.AppendLine("- read_files (до 10 файлов в одном запросе, общий лимит 50000 символов)");
                sb.AppendLine("- list_files");
                sb.AppendLine("- grep (массив patterns — только когда ищешь место по НЕСКОЛЬКИМ файлам)");
                sb.AppendLine("- write_file, write_files, patch_file, rename_file, delete_file, create_directory");
                sb.AppendLine("- run_command (ТОЛЬКО сборка/тесты/компиляция)");
                sb.AppendLine("- update_file_summaries (пишет краткие описания файлов в .leron/file_index.json; вызывай ПОСЛЕ каждого изменения файла)");
                sb.AppendLine("- request_user_input, request_more_steps, request_outside_access, finish");
                sb.AppendLine();
                sb.AppendLine("ПРАВИЛА ФАЙЛОВ (раунд 5):");
                sb.AppendLine("- Новые и короткие файлы (до 100 строк) создавай/пиши через write_file.");
                sb.AppendLine("- Длинные файлы (100+ строк) изменяй ТОЛЬКО через patch_file (замена куска строк); write_file поверх длинного файла запрещён.");
                sb.AppendLine("- Файл пустой или короче 100 строк — можно write_file целиком.");
                sb.AppendLine("- Если заметил ошибку в СВОЁМ же куске (опечатка, сломанный синтаксис, неверное имя) — ОБЯЗАН сразу добавить второй patch_file с исправлением в ТОМ ЖЕ ответе, не откладывая на следующий шаг.");
                sb.AppendLine();
                sb.AppendLine("СБОРКА И КОМАНДЫ (раунд 5):");
                sb.AppendLine("- Сборка проекта запускается ТОЛЬКО автоматически на finish — сам её НЕ запускай.");
                sb.AppendLine("- run_command без подтверждения пользователя разрешён ТОЛЬКО для команды проверки проекта и тестов (npm test / pytest / dotnet test …); любая другая команда уйдёт на подтверждение.");
                sb.AppendLine();
                sb.AppendLine("ЭКОНОМИЯ ЗАПРОСОВ (каждый твой ответ = 1 запрос пользователя):");
                sb.AppendLine("- Файлы читай ТОЛЬКО read_file/read_files: полное содержимое в UTF-8 за один запрос. Файл до 300–400 строк читай сразу целиком, без grep и без чтения по частям.");
                sb.AppendLine("- Чтение/разведка через shell (type/cat/more/head/tail/dir/ls/tree/Get-Content/Select-Object) отключены и вернут заглушку.");
                sb.AppendLine("- Готовые правки отправляй цепочкой в ОДНОМ ответе, finish последним: {\"name\":\"write_file\",\"arguments\":{...}}{\"name\":\"finish\",\"arguments\":{...}} — проверка пройдёт автоматически, без лишнего запроса.");
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
        sb.AppendLine("Если шагов не хватает, вызови request_more_steps.");
        sb.AppendLine("Если нужно выйти за пределы проекта, вызови request_outside_access.");
        sb.AppendLine();
        if (s.Root != null)
        {
            var check = EnsureCheckCommand(s.Root);
            if (!string.IsNullOrWhiteSpace(check))
                sb.AppendLine($"Команда проверки проекта: {check}. Она выполнится автоматически ОДИН раз на finish — сам её НЕ запускай.");
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
        await sem.WaitAsync(abort);
        try
        {
            if (abort.IsCancellationRequested) return (false, "cancelled");
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
                await client.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
                sent = true;
                break;
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
    public async Task<object> HandleApproveAsync(AgentSession s, ApproveRequest req, CancellationToken ct)
    {
        var loopCts = BeginLoop(s.Role, ct);
        try
        {
            if (s.Pending.Count == 0)
                return new { status = "final", role = s.Role, response = "В сессии нет действия, ожидающего подтверждения.", resultStatus = "failed", cards = NewCards(s), stepsUsed = s.StepUsed, stepLimit = s.StepLimit };
            var pt = s.Pending.Peek();
            if (pt.Name == "request_more_steps")
            {
                s.Pending.Dequeue();
                if (!req.Approve) return Finish(s, "Пользователь остановил работу агента.", "failed");
                var add = req.Steps > 0 ? req.Steps : GetInt(pt.Args, "count", 10);
                s.StepLimit += add;
                s.BrowserNextPrompt = $"Пользователь разрешил дополнительные шаги: +{add}. Продолжай работу.";
                return await RunBrowserAgentLoopAsync(s, loopCts.Token);
            }
            if (pt.Name == "request_user_input")
            {
                s.Pending.Dequeue();
                if (!req.Approve || string.IsNullOrWhiteSpace(req.InputText))
                    return Finish(s, "Пользователь не дал ответа. Заверши задачу текстом.", "needs_user");
                var low = req.InputText.ToLowerInvariant();
                // Раунд 5: ответ на карточку «продолжить чинить?» да/нет.
                if (s.RepairMode && low.Contains("→ нет"))
                    return Finish(s, "Пользователь решил остановить ремонт. Задача завершена.", "needs_user");
                if (s.RepairMode && low.Contains("→ да"))
                {
                    s.SameErrorStreak = 0;
                    s.LastCheckError = "";
                    s.RepairAttempts = 0;
                    if (s.StepUsed >= s.StepLimit) s.StepLimit = s.StepUsed + 10;
                    s.BrowserNextPrompt =
                        "Пользователь решил продолжить ремонт. Прочитай ошибку, найди причину (read_file/read_files), " +
                        "сделай минимальный patch_file и вызови finish для повторной проверки.";
                    return await RunBrowserAgentLoopAsync(s, loopCts.Token);
                }
                s.RepairAttempts = 0;
                // Раунд 2: GUI шлёт все ответы одной строкой-пакетом — ИИ получает их сразу.
                s.BrowserNextPrompt = $"Ответы пользователя на твои вопросы:\n{req.InputText}";
                return await RunBrowserAgentLoopAsync(s, loopCts.Token);
            }
            if (pt.Name == "request_outside_access")
            {
                s.Pending.Dequeue();
                var path = GetStr(pt.Args, "path", "");
                var actions = GetStr(pt.Args, "requested_actions", "read");
                if (!req.Approve)
                {
                    s.BrowserNextPrompt = $"Пользователь отказал в выходе за проект: {path}.\nПредложи альтернативу внутри проекта или заверши задачу.";
                    return await RunBrowserAgentLoopAsync(s, loopCts.Token);
                }
                try
                {
                    var full = System.IO.Path.GetFullPath(path);
                    s.OutsideGrants.Add(new OutsideGrant
                    {
                        Path = full,
                        Actions = actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(x => x.ToLowerInvariant()).ToHashSet()
                    });
                    s.BrowserNextPrompt = $"Пользователь разрешил выход за проект: {full}\nРазрешённые действия: {actions}\nРаботай только с этим путём и только с этими действиями.";
                }
                catch (Exception ex)
                {
                    s.BrowserNextPrompt = $"Не удалось выдать доступ: {ex.Message}";
                }
                return await RunBrowserAgentLoopAsync(s, loopCts.Token);
            }
            s.Pending.Dequeue();
            if (!req.Approve)
            {
                s.ToolLog.Add($"{pt.Name} → отклонено пользователем");
                AgentLog($"[TOOL] {pt.Name} → отклонено");
                s.BrowserNextPrompt = "Действие отклонено пользователем. Не повторяй его. Предложи альтернативу или ответь текстом на русском.";
                return await RunBrowserAgentLoopAsync(s, loopCts.Token);
            }
            if (pt.Name == "run_command")
            {
                var cmd = NormCommand(GetStr(pt.Args, "command"));
                if (IsDangerousCommand(cmd)) s.DangerApproved.Add(CommandKey(cmd));
                else if (s.Mode == "auto" || req.Remember) AddAutoRule(CommandKey(cmd));
            }
            else if (IsMutating(pt.Name))
            {
                if (s.Mode == "auto" || req.Remember)
                {
                    var raw = GetStr(pt.Args, "path");
                    var resolved = ResolveSessionPath(s, raw, "write");
                    AddAutoRule(PathRule(pt.Name, resolved ?? raw, s.Root));
                }
            }
            s.BrowserNextPrompt = await ExecuteApprovedToolAsync(s, pt);
            return await RunBrowserAgentLoopAsync(s, loopCts.Token);
        }
        finally
        {
            EndLoop(s.Role, loopCts);
        }
    }
    public bool NeedsAsk(AgentSession s, PendingTool c)
    {
        if (!s.AllowTools) return false;
        if (c.Name == "run_command")
        {
            if (s.Mode is "chat" or "plan") return false;
            var cmd = NormCommand(GetStr(c.Args, "command"));
            if (IsDangerousCommand(cmd))
                return !(s.Mode == "yolo" && s.DangerApproved.Contains(CommandKey(cmd)));
            if (s.Mode == "yolo") return false;
            // Раунд 5: без подтверждения — ТОЛЬКО команда проверки проекта и тесты.
            var check = s.Root == null ? null : EnsureCheckCommand(s.Root);
            if (!string.IsNullOrWhiteSpace(check) && NormCommand(cmd) == NormCommand(check)) return false;
            if (IsTestCommand(cmd)) return false;
            return true;
        }
        if (IsMutating(c.Name))
        {
            if (s.Mode is "chat" or "plan") return false;
            if (s.Mode == "yolo") return false;
            if (s.Mode == "repair") return false;
            var raw = GetStr(c.Args, "path");
            var resolved = ResolveSessionPath(s, raw, "write");
            var rule = PathRule(c.Name, resolved ?? raw, s.Root);
            if (s.Mode == "auto") return !IsAutoApproved(rule);
            return true;
        }
        return false;
    }
}