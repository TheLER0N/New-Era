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
            dangerous = IsDangerousTool(s, c), cards = NewCards(s)
        };
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
                requestedCount = GetInt(c.Args, "count", 4),
                reason = GetStr(c.Args, "reason", "Нужно больше шагов."),
                cards = NewCards(s)
            };
        }
        if (c.Name == "request_user_input")
        {
            return new
            {
                status = "user_input", sessionId = sid, role = s.Role,
                question = GetStr(c.Args, "question", "Нужна дополнительная информация."),
                cards = NewCards(s)
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
                cards = NewCards(s)
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
        if (s.Root != null) sb.AppendLine($"Корень проекта: {s.Root}");
        if (s.AllowTools)
        {
            if (s.Mode == "plan")
            {
                sb.AppendLine("Режим: планирование. Разрешены только read_file, list_files, grep.");
            }
            else
            {
                sb.AppendLine("Доступные инструменты:");
                sb.AppendLine("- read_file"); sb.AppendLine("- list_files"); sb.AppendLine("- grep");
                sb.AppendLine("- write_file"); sb.AppendLine("- patch_file"); sb.AppendLine("- rename_file");
                sb.AppendLine("- delete_file"); sb.AppendLine("- create_directory"); sb.AppendLine("- run_command");
                sb.AppendLine("- request_user_input"); sb.AppendLine("- request_more_steps");
                sb.AppendLine("- request_outside_access"); sb.AppendLine("- finish");
            }
        }
        else
        {
            sb.AppendLine("Режим: обычный чат. Инструменты недоступны.");
        }
        sb.AppendLine();
        sb.AppendLine($"Текущий лимит шагов: {s.StepLimit}. Использовано: {s.StepUsed}.");
        sb.AppendLine("Если шагов не хватает, вызови request_more_steps.");
        sb.AppendLine("Если нужно выйти за пределы проекта, вызови request_outside_access.");
        sb.AppendLine();
        if (s.Root != null)
        {
            var check = EnsureCheckCommand(s.Root);
            if (!string.IsNullOrWhiteSpace(check))
                sb.AppendLine($"Команда проверки проекта: {check}. После изменений проверка запускается автоматически.");
        }
        if (s.Mode == "repair")
            sb.AppendLine("Режим ремонта: исправь одну ошибку минимально, затем повтори проверку.");
        sb.AppendLine();
        sb.AppendLine("Если нужно действие, ответь строго одним JSON-блоком:");
        sb.AppendLine("{\"name\":\"...\",\"arguments\":{...}}");
        sb.AppendLine("Если действие не нужно, ответь текстом на русском.");
        sb.AppendLine();
        return sb.ToString();
    }

    public string ToolResultPrompt(string name, string result) =>
        $"Результат выполнения {name}:\n{Truncate(result, 6000)}\n" +
        "Если задача завершена — ответь текстом на русском или вызови finish.\n" +
        "Если нужны ещё действия — снова один JSON-блок с инструментом.";

    public async Task<object> RunBrowserAgentLoopAsync(AgentSession s, CancellationToken loopToken)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            if (loopToken.IsCancellationRequested)
                return Finish(s, "⏹ Цикл агента завершён (новая задача или отмена).", "failed");
            if (s.StepUsed >= s.StepLimit)
            {
                return Finish(s,
                    "Лимит шагов закончился. Если нужно продолжить, запроси дополнительные шаги через request_more_steps.",
                    "needs_user");
            }
            while (s.Pending.Count > 0)
            {
                var head = s.Pending.Peek();
                if (IsSpecial(head.Name)) return PauseSpecial(s, head);
                if (NeedsAsk(s, head)) return ApprovalPause(s, head);
                s.Pending.Dequeue();
                s.BrowserNextPrompt = await ExecuteApprovedToolAsync(s, head);
            }
            string prompt;
            if (!string.IsNullOrEmpty(s.BrowserNextPrompt))
            {
                prompt = s.BrowserNextPrompt;
                s.BrowserNextPrompt = "";
            }
            else
            {
                var userText = s.Messages.Count > 0
                    ? s.Messages[s.Messages.Count - 1]?["content"]?.GetValue<string>() ?? "" : "";
                // полная инструкция — один раз на сессию чата, дальше только текст задачи
                if (!InstructionSent.TryGetValue(s.Role, out var sent) || !sent)
                {
                    prompt = BrowserInstruction(s) + userText;
                    InstructionSent[s.Role] = true;
                }
                else
                {
                    prompt = userText;
                }
            }
            var reqId = NextReqId();
            AgentLog($"[BROWSER] шаг {s.StepUsed + 1} reqid={reqId}: {Truncate(prompt, 220)}");
            var (ok, text) = await SendToBrowserAndWait(s.Role, prompt, s.Think, 300000, loopToken, reqId);
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
            var parsed = TryParseTextToolCall(text);
            if (parsed == null)
                return Finish(s, StripProviderMetadata(text), "success");
            var c = new PendingTool
            {
                Id = Guid.NewGuid().ToString(),
                Name = parsed.Value.name,
                Args = parsed.Value.args
            };
            if (!s.AllowTools && !IsSpecial(c.Name))
                return Finish(s, "Инструменты недоступны в этом режиме.", "failed");
            if (c.Name == "finish")
            {
                return Finish(s,
                    GetStr(c.Args, "summary", "Задача завершена."),
                    GetStr(c.Args, "status", "success"));
            }
            if (IsSpecial(c.Name))
            {
                s.Pending.Enqueue(c);
                return PauseSpecial(s, c);
            }
            if (NeedsAsk(s, c))
            {
                s.Pending.Enqueue(c);
                return ApprovalPause(s, c);
            }
            s.BrowserNextPrompt = await ExecuteApprovedToolAsync(s, c);
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
            var tcs = new TaskCompletionSource<string>();
            PendingResponses[role] = tcs;
            var cts = new CancellationTokenSource();
            PendingCancels[role] = cts;
            using var abortReg = abort.Register(() => tcs.TrySetCanceled());
            var chatId = RoleChatMap[role];
            var url = Config.Roles.TryGetValue(role, out var roleCfg) && !string.IsNullOrEmpty(roleCfg.Url)
                ? roleCfg.Url : $"https://chat.qwen.ai/c/{chatId}";
            var payload = Encoding.UTF8.GetBytes(
                $"TYPE:{role}|{chatId}|{url}|{(think ? "1" : "0")}|{reqId}|{text}");
            LastTypePayload[role] = payload;
            // Один запрос = один получатель: шлём только ПЕРВОМУ живому клиенту,
            // иначе несколько сокетов продублировали бы одно и то же сообщение в Qwen.
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
            var delayTask = Task.Delay(timeoutMs);
            var cancelTask = Task.Run(async () => { try { await Task.Delay(Timeout.Infinite, cts.Token); } catch { } });
            var done = await Task.WhenAny(tcs.Task, delayTask, cancelTask);
            PendingResponses.TryRemove(role, out _);
            PendingCancels.TryRemove(role, out _);
            LastSentText.TryRemove(role, out _);
            if (done == tcs.Task)
            {
                if (tcs.Task.IsCanceled || abort.IsCancellationRequested) return (false, "cancelled");
                return (true, await tcs.Task);
            }
            if (done == cancelTask) return (false, "cancelled");
            return (false, "Браузер не ответил за 300 секунд.");
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
                return new { status = "final", role = s.Role, response = "В сессии нет действия, ожидающего подтверждения.", resultStatus = "failed", cards = NewCards(s) };
            var pt = s.Pending.Peek();
            if (pt.Name == "request_more_steps")
            {
                s.Pending.Dequeue();
                if (!req.Approve) return Finish(s, "Пользователь остановил работу агента.", "failed");
                var add = req.Steps > 0 ? req.Steps : GetInt(pt.Args, "count", 4);
                s.StepLimit += add;
                s.BrowserNextPrompt = $"Пользователь разрешил дополнительные шаги: +{add}. Продолжай работу.";
                return await RunBrowserAgentLoopAsync(s, loopCts.Token);
            }
            if (pt.Name == "request_user_input")
            {
                s.Pending.Dequeue();
                if (!req.Approve || string.IsNullOrWhiteSpace(req.InputText))
                    return Finish(s, "Пользователь не дал ответа. Заверши задачу текстом.", "needs_user");
                s.BrowserNextPrompt = $"Ответ пользователя на твой вопрос:\n{req.InputText}";
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
            if (s.Mode == "auto") return !IsAutoApproved(CommandKey(cmd));
            if (s.Mode == "repair")
            {
                var check = s.Root == null ? null : EnsureCheckCommand(s.Root);
                if (!string.IsNullOrWhiteSpace(check) && NormCommand(cmd) == NormCommand(check))
                    return false;
                return !IsAutoApproved(CommandKey(cmd));
            }
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