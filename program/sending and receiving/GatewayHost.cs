using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace MainApp;

public static class GatewayHost
{
    private static string FindSendReceivingDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "sending and receiving")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "sending and receiving")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "sending and receiving")),
            Path.Combine(baseDir, "sending and receiving")
        };
        foreach (var c in candidates)
        {
            try { if (Directory.Exists(c)) return c; } catch { }
        }
        return candidates[0];
    }

    public static void Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://localhost:51234");
        var app = builder.Build();

        var clients = new List<WebSocket>();

        var srDir = FindSendReceivingDir();
        var configPath = Path.Combine(srDir, "config.json");
        if (!File.Exists(configPath)) configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var chatRoleMap = new Dictionary<string, string>();
        var roleChatMap = new Dictionary<string, string>();
        foreach (var role in config.Roles)
        {
            if (!string.IsNullOrEmpty(role.Value.ChatId))
            {
                chatRoleMap[role.Value.ChatId] = role.Key;
                roleChatMap[role.Key] = role.Value.ChatId;
            }
        }

        var pendingResponses = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
        var pendingCancels = new ConcurrentDictionary<string, CancellationTokenSource>();
        var roleSendLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        var lastSentText = new ConcurrentDictionary<string, string>();
        var lastAiText = new ConcurrentDictionary<string, string>();
        var lastExtDiag = new ConcurrentDictionary<string, string>();

        var logsDir = Path.Combine(srDir, "logs");
        // Звук при получении ответа
void PlayNotifySound()
{
    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
}

// Показать окно браузера при капче
void ShowBrowserForCaptcha()
{
    try
    {
        var proc = System.Diagnostics.Process.GetProcessesByName("chrome")
            .Concat(System.Diagnostics.Process.GetProcessesByName("msedge"))
            .FirstOrDefault(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
        if (proc != null)
        {
            ShowWindow(proc.MainWindowHandle, 5); // SW_SHOW = 5
            SetForegroundWindow(proc.MainWindowHandle);
        }
    }
    catch { }
}

        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        try { if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir); }
        catch { logsDir = Path.Combine(AppContext.BaseDirectory, "logs"); Directory.CreateDirectory(logsDir); }

        void LogRole(string role, string message)
        {
            var logFile = Path.Combine(logsDir, $"{role}_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }

        void AgentLog(string message)
        {
            try
            {
                var logFile = Path.Combine(logsDir, $"agent_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        void SaveConfig()
        {
            try
            {
                File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        var agentHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        const string AgentBaseUrl = "https://qwen.aikit.club/v1";
        const string AgentModel = "qwen3.6-plus";

        var agentSessions = new ConcurrentDictionary<string, AgentSession>();
        var autoLock = new object();
        var autoApproved = new HashSet<string>(config.AutoApprove ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        string? ResolveAgentToken()
        {
            if (!string.IsNullOrWhiteSpace(config.Token)) return config.Token;
            var env = Environment.GetEnvironmentVariable("QWEN_API_KEY") ?? Environment.GetEnvironmentVariable("QWEN_AIKIT_API_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            var credPaths = new[] {
                Path.Combine(AppContext.BaseDirectory, "credentials.json"),
                Path.Combine(srDir, "credentials.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "credentials.json"),
                Path.GetFullPath(Path.Combine(srDir, "..", "credentials.json"))
            };
            foreach (var p in credPaths) {
                if (File.Exists(p)) {
                    try {
                        var json = File.ReadAllText(p);
                        var node = JsonNode.Parse(json);
                        var token = node?["sessions"]?[0]?["qwen_credentials"]?["access_token"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(token)) return token;
                    } catch {}
                }
            }
            return null;
        }

        static bool IsKnownTool(string name) =>
            name is "read_file" or "list_files" or "write_file" or "edit_file" or "delete_file" or "create_directory";

        static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";

        static string StripProviderMetadata(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var idx = text.LastIndexOf("<details>", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var end = text.IndexOf("</details>", idx, StringComparison.OrdinalIgnoreCase);
                if (end >= 0)
                    text = text.Substring(0, idx) + text.Substring(end + "</details>".Length);
            }
            return text.Trim();
        }

        static bool ModeAllowsEdit(string mode) => mode is "edit" or "auto" or "yolo";
        static bool ModeRequiresApproval(string mode) => mode is "edit" or "auto";
        static bool IsMutating(string name) => name is "write_file" or "edit_file" or "delete_file" or "create_directory";

        static string? ResolveSafePath(string raw, string root)
        {
            try
            {
                var trimmed = raw.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(trimmed)) return null;
                trimmed = trimmed.Replace('/', Path.DirectorySeparatorChar);
                var rootFull = Path.GetFullPath(root);
                string full = Path.IsPathRooted(trimmed)
                    ? Path.GetFullPath(trimmed)
                    : Path.GetFullPath(Path.Combine(rootFull, trimmed));
                if (!full.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
                    !full.StartsWith(rootFull + Path.DirectorySeparatorChar))
                    return null;
                return full;
            }
            catch
            {
                return null;
            }
        }

        static (string result, string log) ExecuteAgentTool(string name, JsonObject args, string root, bool allowEdit)
        {
            string Arg(string key) => args[key]?.GetValue<string>() ?? "";
            bool mutating = IsMutating(name);
            if (mutating && !allowEdit)
                return ("Запрещено: текущий режим не позволяет изменять файлы.", $"{name} → запрещено режимом");
            try
            {
                switch (name)
                {
                    case "read_file":
                    {
                        var p = ResolveSafePath(Arg("path"), root);
                        if (p == null) return ("Доступ отклонён: путь вне проекта.", $"{name} {Arg("path")} → отклонено");
                        if (!File.Exists(p)) return ($"Файл не найден: {Arg("path")}", $"{name} {Arg("path")} → не найден");
                        var text = File.ReadAllText(p);
                        if (text.Length > 20000) text = text.Substring(0, 20000) + "\n…[обрезано]";
                        return (text, $"{name} {Arg("path")} → OK");
                    }
                    case "list_files":
                    {
                        var rawPath = string.IsNullOrWhiteSpace(Arg("path")) ? "." : Arg("path");
                        var p = ResolveSafePath(rawPath, root);
                        if (p == null) return ("Доступ отклонён: путь вне проекта.", $"{name} {rawPath} → отклонено");
                        if (!Directory.Exists(p)) return ($"Папка не найдена: {rawPath}", $"{name} {rawPath} → не найдена");
                        var sb = new StringBuilder();
                        foreach (var d in Directory.GetDirectories(p).OrderBy(x => x))
                            sb.AppendLine("<DIR> " + Path.GetFileName(d));
                        foreach (var f in Directory.GetFiles(p).OrderBy(x => x))
                            sb.AppendLine(Path.GetFileName(f));
                        var list = sb.ToString();
                        if (string.IsNullOrWhiteSpace(list)) list = "(пусто)";
                        return (list, $"{name} {rawPath} → OK");
                    }
                    case "create_directory":
                    {
                        var p = ResolveSafePath(Arg("path"), root);
                        if (p == null) return ("Доступ отклонён: путь вне проекта.", $"{name} {Arg("path")} → отклонено");
                        Directory.CreateDirectory(p);
                        return ($"Папка создана: {Arg("path")}", $"{name} {Arg("path")} → OK");
                    }
                    case "write_file":
                    {
                        var p = ResolveSafePath(Arg("path"), root);
                        if (p == null) return ("Доступ отклонён: путь вне проекта.", $"{name} {Arg("path")} → отклонено");
                        var dir = Path.GetDirectoryName(p);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(p, Arg("content"));
                        return ($"Файл записан: {Arg("path")}", $"{name} {Arg("path")} → OK");
                    }
                    case "edit_file":
                    {
                        var p = ResolveSafePath(Arg("path"), root);
                        if (p == null) return ("Доступ отклонён: путь вне проекта.", $"{name} {Arg("path")} → отклонено");
                        if (!File.Exists(p)) return ($"Файл не найден: {Arg("path")}", $"{name} {Arg("path")} → не найден");
                        var text = File.ReadAllText(p);
                        var old = Arg("old_text");
                        if (string.IsNullOrEmpty(old) || !text.Contains(old))
                            return ("old_text не найден в файле. Прочитай файл read_file и попробуй снова.", $"{name} {Arg("path")} → old_text не найден");
                        File.WriteAllText(p, text.Replace(old, Arg("new_text")));
                        return ($"Файл изменён: {Arg("path")}", $"{name} {Arg("path")} → OK");
                    }
                    case "delete_file":
                    {
                        var p = ResolveSafePath(Arg("path"), root);
                        if (p == null) return ("Доступ отклонён: путь вне проекта.", $"{name} {Arg("path")} → отклонено");
                        if (File.Exists(p)) { File.Delete(p); return ($"Файл удалён: {Arg("path")}", $"{name} {Arg("path")} → OK"); }
                        if (Directory.Exists(p)) { Directory.Delete(p, true); return ($"Папка удалена: {Arg("path")}", $"{name} {Arg("path")} → OK"); }
                        return ($"Не найдено: {Arg("path")}", $"{name} {Arg("path")} → не найдено");
                    }
                    default:
                        return ($"Неизвестный инструмент: {name}", $"{name} → неизвестный");
                }
            }
            catch (Exception ex)
            {
                return ("Ошибка выполнения инструмента: " + ex.Message, $"{name} → ошибка");
            }
        }

        static JsonArray BuildTools(bool allowEdit)
        {
            JsonObject Str(string desc) => new() { ["type"] = "string", ["description"] = desc };
            JsonObject Fn(string name, string desc, JsonObject props, JsonArray req) => new()
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["description"] = desc,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = props,
                        ["required"] = req
                    }
                }
            };
            var tools = new JsonArray
            {
                Fn("read_file", "Прочитать файл внутри проекта. Возвращает текст файла.",
                    new JsonObject { ["path"] = Str("Путь относительно корня проекта, например ./src/main.py") },
                    new JsonArray { "path" }),
                Fn("list_files", "Показать файлы и папки внутри папки проекта.",
                    new JsonObject { ["path"] = Str("Путь относительно корня проекта, например . или ./src") },
                    new JsonArray { "path" })
            };
            if (allowEdit)
            {
                tools.Add(Fn("write_file", "Создать или полностью перезаписать файл внутри проекта.",
                    new JsonObject
                    {
                        ["path"] = Str("Путь относительно корня проекта"),
                        ["content"] = Str("Полное текстовое содержимое файла")
                    },
                    new JsonArray { "path", "content" }));
                tools.Add(Fn("edit_file", "Заменить точный фрагмент текста в существующем файле. Сначала прочитай файл.",
                    new JsonObject
                    {
                        ["path"] = Str("Путь относительно корня проекта"),
                        ["old_text"] = Str("Точный фрагмент текста для замены"),
                        ["new_text"] = Str("Текст, на который заменить")
                    },
                    new JsonArray { "path", "old_text", "new_text" }));
                tools.Add(Fn("delete_file", "Удалить файл или папку внутри проекта.",
                    new JsonObject { ["path"] = Str("Путь относительно корня проекта") },
                    new JsonArray { "path" }));
                tools.Add(Fn("create_directory", "Создать папку внутри проекта.",
                    new JsonObject { ["path"] = Str("Путь относительно корня проекта") },
                    new JsonArray { "path" }));
            }
            return tools;
        }

        static JsonObject BuildSystemMessage(string? root, bool allowEdit, bool think)
        {
            var sb = new StringBuilder();
            if (root == null)
            {
                sb.AppendLine("Ты полезный AI-помощник. Всегда отвечай на русском языке, коротко и по делу.");
                return new JsonObject { ["role"] = "system", ["content"] = sb.ToString() };
            }
            sb.AppendLine($"Ты русскоязычный coding agent. Always treat {root} as the only project root.");
            sb.AppendLine($"Resolve all relative paths against {root}.");
            sb.AppendLine("Never create, read, or modify files outside the project root unless the user explicitly asks for an absolute path outside the project.");
            sb.AppendLine("Всегда отвечай на русском языке, если пользователь явно не попросил другой язык.");
            sb.AppendLine("Если пользователь пишет на русском, не переходи на английский.");
            sb.AppendLine("Не добавляй HTML-блоки, details, summary, Response ID или Request ID в ответы.");
            if (!allowEdit)
                sb.AppendLine("Режим: обсуждение и планирование. Разрешено только читать и смотреть файлы, изменять запрещено.");
            sb.AppendLine(think
                ? "Режим ответа: с мышлением. Можно рассуждать, но итог пиши на русском."
                : "Режим ответа: быстрый. Отвечай сразу по делу, без долгих рассуждений.");
            sb.AppendLine("Если нужно действие с файлом — ответь строго одним блоком ```json {\"name\": \"имя_инструмента\", \"arguments\": {...}} ``` без лишнего текста.");
            sb.AppendLine("Если действие не нужно — ответь коротко на русском.");
            sb.AppendLine();
            return new JsonObject { ["role"] = "system", ["content"] = sb.ToString() };
        }

        static (string name, JsonObject args)? TryParseTextToolCall(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var candidates = new List<string>();
            var fence = Regex.Match(text, "```(?:json)?\\s*(\\{[\\s\\S]*?\\})\\s*```");
            if (fence.Success) candidates.Add(fence.Groups[1].Value);
            candidates.Add(text.Trim());
            foreach (var c in candidates)
            {
                try
                {
                    var node = JsonNode.Parse(c) as JsonObject;
                    if (node == null) continue;
                    var name = node["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name) || !IsKnownTool(name)) continue;
                    var args = node["arguments"] as JsonObject;
                    if (args == null)
                    {
                        var argsStr = node["arguments"]?.GetValue<string>();
                        args = argsStr != null ? JsonNode.Parse(argsStr) as JsonObject : null;
                    }
                    return (name, args ?? new JsonObject());
                }
                catch { }
            }
            return null;
        }

        async Task<(bool ok, int code, string body)> CallApi(JsonObject payload, string token)
        {
            try
            {
                var httpReq = new HttpRequestMessage(HttpMethod.Post, AgentBaseUrl + "/chat/completions");
                httpReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                httpReq.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                httpReq.Headers.TryAddWithoutValidation("Origin", "https://chat.qwen.ai");
                httpReq.Headers.TryAddWithoutValidation("Referer", "https://chat.qwen.ai/");
                httpReq.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                httpReq.Headers.TryAddWithoutValidation("Accept", "application/json");
                httpReq.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
                var resp = await agentHttp.SendAsync(httpReq);
                var respBody = await resp.Content.ReadAsStringAsync();
                return (resp.IsSuccessStatusCode, (int)resp.StatusCode, respBody);
            }
            catch (Exception ex)
            {
                return (false, 0, ex.Message);
            }
        }

        bool NeedsAsk(AgentSession s, string toolName) =>
            s.AllowEdit && IsMutating(toolName) && ModeRequiresApproval(s.Mode) &&
            !(s.Mode == "auto" && autoApproved.Contains(toolName));

        JsonObject MakePayload(AgentSession s)
        {
            var payload = new JsonObject
            {
                ["model"] = AgentModel,
                ["messages"] = s.Messages.DeepClone(),
                ["temperature"] = 0.7,
                ["stream"] = false
            };
            if (s.UseTools)
            {
                payload["tools"] = BuildTools(s.AllowEdit).DeepClone();
                payload["tool_choice"] = "auto";
            }
            return payload;
        }

        object Finish(AgentSession s, string text)
        {
            AgentLog($"[FINAL] {Truncate(text, 500)}");
            if (!string.IsNullOrEmpty(s.Role))
                LogRole(s.Role, $"[AGENT]: {Truncate(text, 300)}");
            return new { status = "final", role = s.Role, response = text, tools = s.ToolLog };
        }

        object ApprovalPause(AgentSession s, PendingTool c)
        {
            var sid = Guid.NewGuid().ToString("N");
            agentSessions[sid] = s;
            return new
            {
                status = "approval",
                sessionId = sid,
                role = s.Role,
                tool = c.Name,
                arguments = c.Args.ToJsonString(),
                tools = s.ToolLog
            };
        }

        async Task<object> RunAgentLoopAsync(AgentSession s, string token)
        {
            for (int step = 0; step < 8; step++)
            {
                while (s.Pending.Count > 0)
                {
                    var head = s.Pending.Peek();
                    if (NeedsAsk(s, head.Name))
                        return ApprovalPause(s, head);
                    s.Pending.Dequeue();
                    var (hr, hlg) = ExecuteAgentTool(head.Name, head.Args, s.Root ?? "", s.AllowEdit);
                    s.ToolLog.Add(hlg);
                    AgentLog($"[TOOL] {hlg}");
                    s.Messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = head.Id, ["content"] = hr });
                }
                var (ok, code, respBody) = await CallApi(MakePayload(s), token);
                if (!ok && s.UseTools && step == 0)
                {
                    AgentLog($"[API] tools отклонены ({code}), повтор без tools");
                    s.UseTools = false;
                    (ok, code, respBody) = await CallApi(MakePayload(s), token);
                }
                if (!ok)
                {
                    AgentLog($"[API ERROR] {code} {Truncate(respBody, 300)}");
                    return Finish(s, $"Ошибка API {code}: {Truncate(respBody, 300)}");
                }
                var respNode = JsonNode.Parse(respBody);
                var message = respNode?["choices"]?.AsArray()[0]?["message"] as JsonObject;
                if (message == null)
                    return Finish(s, "Пустой ответ модели.");
                var content = message["content"]?.GetValue<string>() ?? "";
                var calls = new List<PendingTool>();
                var toolCalls = message["tool_calls"] as JsonArray;
                if (toolCalls != null)
                {
                    foreach (var tc in toolCalls)
                    {
                        var id = tc?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                        var name = tc?["function"]?["name"]?.GetValue<string>() ?? "";
                        var argsRaw = tc?["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                        JsonObject args;
                        try { args = JsonNode.Parse(argsRaw) as JsonObject ?? new JsonObject(); }
                        catch { args = new JsonObject(); }
                        calls.Add(new PendingTool { Id = id, Name = name, Args = args });
                    }
                }
                else
                {
                    var parsed = TryParseTextToolCall(content);
                    if (parsed != null)
                        calls.Add(new PendingTool { Id = Guid.NewGuid().ToString(), Name = parsed.Value.name, Args = parsed.Value.args });
                }
                if (calls.Count > 0)
                {
                    s.Messages.Add((JsonObject)message.DeepClone());
                    for (int i = 0; i < calls.Count; i++)
                    {
                        var c = calls[i];
                        if (NeedsAsk(s, c.Name))
                        {
                            for (int j = i; j < calls.Count; j++) s.Pending.Enqueue(calls[j]);
                            return ApprovalPause(s, c);
                        }
                        var (r, lg) = ExecuteAgentTool(c.Name, c.Args, s.Root ?? "", s.AllowEdit);
                        s.ToolLog.Add(lg);
                        AgentLog($"[TOOL] {lg}");
                        s.Messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = c.Id, ["content"] = r });
                    }
                    continue;
                }
                return Finish(s, StripProviderMetadata(content));
            }
            return Finish(s, s.ToolLog.Count > 0
                ? "Агент завершил работу. Детали — в строках инструментов выше."
                : "Ответ не получен.");
        }

        async Task<(bool ok, string text)> SendToBrowserAndWait(string role, string text, bool think, int timeoutMs, CancellationToken httpAbort = default)
        {
            var sem = roleSendLocks.GetOrAdd(role, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(httpAbort);
            try
            {
                if (!roleChatMap.ContainsKey(role))
                    return (false, $"Роль '{role}' не закреплена за чатом. Закрепи через popup плагина.");
                lastSentText[role] = text;
                AgentLog($"[SEND] роль={role} текст={Truncate(text, 120)}");
                LogRole(role, $"[USER]: {Truncate(text, 300)}");
                var tcs = new TaskCompletionSource<string>();
                pendingResponses[role] = tcs;
                var cts = new CancellationTokenSource();
                pendingCancels[role] = cts;
                using var abortReg = httpAbort.Register(() => tcs.TrySetCanceled());
                var chatId = roleChatMap[role];
                var url = config.Roles.TryGetValue(role, out var roleCfg) && !string.IsNullOrEmpty(roleCfg.Url)
                    ? roleCfg.Url
                    : $"https://chat.qwen.ai/c/{chatId}";
                var payload = Encoding.UTF8.GetBytes($"TYPE:{role}|{chatId}|{url}|{(think ? "1" : "0")}|{text}");
                bool sent = false;
                foreach (var client in clients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        await client.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
                        sent = true;
                    }
                }
                if (!sent)
                {
                    pendingResponses.TryRemove(role, out _);
                    pendingCancels.TryRemove(role, out _);
                    return (false, "Расширение браузера не подключено. Открой браузер LERON и дождись подключения.");
                }
                var delayTask = Task.Delay(timeoutMs);
                var cancelTask = Task.Run(async () => { try { await Task.Delay(Timeout.Infinite, cts.Token); } catch { } });
                var done = await Task.WhenAny(tcs.Task, delayTask, cancelTask);
                pendingResponses.TryRemove(role, out _);
                pendingCancels.TryRemove(role, out _);
                lastSentText.TryRemove(role, out _);
                if (done == tcs.Task)
                {
                    if (tcs.Task.IsCanceled || httpAbort.IsCancellationRequested)
                        return (false, "Клиент отключился");
                    return (true, await tcs.Task);
                }
                if (done == cancelTask) return (false, "cancelled");
                return (false, "Браузер не ответил за 120 секунд.");
            }
            finally
            {
                sem.Release();
            }
        }

        string BrowserInstruction(AgentSession s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Ты локальный русскоязычный coding agent, подключённый к LERON CLI.");
            if (s.Root != null)
                sb.AppendLine($"Корень проекта: {s.Root}. Все относительные пути решай от корня проекта.");
            sb.AppendLine(s.AllowEdit
                ? "Доступные инструменты: read_file, list_files, write_file, edit_file, delete_file, create_directory."
                : "Доступные инструменты: read_file, list_files. Изменять файлы запрещено.");
            sb.AppendLine(s.Think
                ? "Режим ответа: с мышлением. Можно рассуждать, но итог пиши на русском."
                : "Режим ответа: быстрый. Отвечай сразу по делу, без долгих рассуждений.");
            sb.AppendLine("Если нужно действие с файлом — ответь строго одним блоком ```json {\"name\": \"имя_инструмента\", \"arguments\": {...}} ``` без лишнего текста.");
            sb.AppendLine("Если действие не нужно — ответь коротко на русском.");
            sb.AppendLine();
            return sb.ToString();
        }

        string ToolResultPrompt(string name, string result) =>
            $"Результат выполнения {name}: {Truncate(result, 4000)}\n" +
            "Если задача завершена — ответь текстом на русском. Если нужны ещё действия — снова один JSON-блок с инструментом.";

        async Task<object> RunBrowserAgentLoopAsync(AgentSession s, CancellationToken httpAbort)
        {
            for (int step = 0; step < 8; step++)
            {
                while (s.Pending.Count > 0)
                {
                    var head = s.Pending.Peek();
                    if (NeedsAsk(s, head.Name))
                        return ApprovalPause(s, head);
                    s.Pending.Dequeue();
                    var (hr, hlg) = ExecuteAgentTool(head.Name, head.Args, s.Root ?? "", s.AllowEdit);
                    s.ToolLog.Add(hlg);
                    AgentLog($"[TOOL] {hlg}");
                    s.BrowserNextPrompt = ToolResultPrompt(head.Name, hr);
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
                        ? s.Messages[s.Messages.Count - 1]?["content"]?.GetValue<string>() ?? ""
                        : "";
                    prompt = BrowserInstruction(s) + userText;
                }
                AgentLog($"[BROWSER] шаг {step}: {Truncate(prompt, 200)}");
                var (ok, text) = await SendToBrowserAndWait(s.Role, prompt, s.Think, 120000, httpAbort);
                if (!ok)
                {
                    AgentLog($"[BROWSER ERROR] {text}");
                    if (text == "cancelled")
                        return Finish(s, "⏹ Отменено пользователем.");
                    return Finish(s, s.ToolLog.Count > 0
                        ? $"⚠ {text} Выполнено действий: {s.ToolLog.Count}. Детали в строках инструментов."
                        : $"⚠ {text}");
                }
                var parsed = TryParseTextToolCall(text);
                if (parsed == null)
                    return Finish(s, StripProviderMetadata(text));
                var c = new PendingTool
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = parsed.Value.name,
                    Args = parsed.Value.args
                };
                if (NeedsAsk(s, c.Name))
                {
                    s.Pending.Enqueue(c);
                    return ApprovalPause(s, c);
                }
                var (r, lg) = ExecuteAgentTool(c.Name, c.Args, s.Root ?? "", s.AllowEdit);
                s.ToolLog.Add(lg);
                AgentLog($"[TOOL] {lg}");
                s.BrowserNextPrompt = ToolResultPrompt(c.Name, r);
            }
            return Finish(s, "Агент завершил работу по лимиту шагов. Детали в строках инструментов.");
        }

        app.MapPost("/agent-run", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<AgentRequest>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (req == null || string.IsNullOrWhiteSpace(req.Text))
                return Results.BadRequest(new { error = "text is required" });
            var mode = string.IsNullOrWhiteSpace(req.Mode) ? "edit" : req.Mode.ToLowerInvariant();
            string? root = null;
            if (!string.IsNullOrWhiteSpace(req.ProjectPath))
            {
                if (!Directory.Exists(req.ProjectPath))
                    return Results.BadRequest(new { error = "Папка проекта не найдена. Выбери проект в хабе заново." });
                root = req.ProjectPath;
            }
            var token = ResolveAgentToken();
            var s = new AgentSession
            {
                Role = req.Role,
                Root = root,
                Mode = mode,
                Think = req.Think,
                AllowEdit = root != null && ModeAllowsEdit(mode),
                UseTools = root != null,
                Backend = token != null ? "api" : "browser"
            };
            s.Messages.Add(new JsonObject { ["role"] = "user", ["content"] = req.Text });
            if (token != null)
            {
                AgentLog("[BACKEND] прямой API");
                s.Messages.Insert(0, BuildSystemMessage(root, s.AllowEdit, s.Think));
                return Results.Ok(await RunAgentLoopAsync(s, token));
            }
            AgentLog("[BACKEND] браузер");
            return Results.Ok(await RunBrowserAgentLoopAsync(s, ctx.RequestAborted));
        });

        app.MapPost("/agent-approve", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<ApproveRequest>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (req == null || string.IsNullOrWhiteSpace(req.SessionId))
                return Results.BadRequest(new { error = "sessionId is required" });
            if (!agentSessions.TryRemove(req.SessionId, out var s))
                return Results.BadRequest(new { error = "Сессия не найдена или устарела." });
            if (s.Pending.Count == 0)
                return Results.BadRequest(new { error = "В сессии нет действия, ожидающего подтверждения." });
            var pt = s.Pending.Dequeue();
            if (req.Approve)
            {
                if (s.Mode == "auto" || req.Remember)
                {
                    bool added;
                    lock (autoLock) added = autoApproved.Add(pt.Name);
                    if (added)
                    {
                        config.AutoApprove = autoApproved.ToList();
                        SaveConfig();
                        AgentLog($"[AUTO] инструмент {pt.Name} будет подтверждаться автоматически");
                    }
                }
                var (r, lg) = ExecuteAgentTool(pt.Name, pt.Args, s.Root ?? "", true);
                s.ToolLog.Add(lg);
                AgentLog($"[TOOL] {lg}");
                if (s.Backend == "browser")
                    s.BrowserNextPrompt = ToolResultPrompt(pt.Name, r);
                else
                    s.Messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = pt.Id, ["content"] = r });
            }
            else
            {
                s.ToolLog.Add($"{pt.Name} → отклонено пользователем");
                AgentLog($"[TOOL] {pt.Name} → отклонено");
                if (s.Backend == "browser")
                    s.BrowserNextPrompt = "Действие отклонено пользователем. Не повторяй его, предложи альтернативу или ответь текстом на русском.";
                else
                    s.Messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = pt.Id,
                        ["content"] = "Действие отклонено пользователем. Не повторяй его, предложи альтернативу или объясни."
                    });
            }
            if (s.Backend == "browser")
                return Results.Ok(await RunBrowserAgentLoopAsync(s, ctx.RequestAborted));
            var token = ResolveAgentToken();
            if (token == null)
                return Results.BadRequest(new { error = "Нет API-токена." });
            return Results.Ok(await RunAgentLoopAsync(s, token));
        });

        app.MapPost("/cancel", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<CancelRequest>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (req == null || string.IsNullOrWhiteSpace(req.Role))
                return Results.BadRequest(new { error = "role is required" });
            if (pendingCancels.TryRemove(req.Role, out var cts))
                cts.Cancel();
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/diag", async (HttpContext ctx) =>
        {
            foreach (var client in clients)
            {
                try
                {
                    if (client.State == WebSocketState.Open)
                        await client.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("DIAG?")), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
            await Task.Delay(700);
            return Results.Ok(new
            {
                gateway = "ok",
                ws_clients = clients.Count,
                extension = lastExtDiag.ToDictionary(kv => kv.Key, kv => kv.Value),
                pending_wait_roles = pendingResponses.Keys.ToArray(),
                last_sent = lastSentText.ToDictionary(kv => kv.Key, kv => Truncate(kv.Value, 100)),
                last_ai_received = lastAiText.ToDictionary(kv => kv.Key, kv => Truncate(kv.Value, 100)),
                roles = roleChatMap,
                token_present = !string.IsNullOrWhiteSpace(config.Token)
            });
        });

        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                clients.Add(webSocket);
                var buffer = new byte[1024 * 4];
                while (webSocket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (result.MessageType == WebSocketMessageType.Close)
                            break;
                        await ms.WriteAsync(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    var msg = Encoding.UTF8.GetString(ms.ToArray());
                    if (msg.StartsWith("AI:"))
                    {
                        var parts = msg.Substring(3).Split('|', 2);
                        var chatId = parts.Length > 1 ? parts[0] : "unknown";
                        var text = parts.Length > 1 ? parts[1] : parts[0];
                        var role = chatRoleMap.ContainsKey(chatId) ? chatRoleMap[chatId] : "unknown";
                        lastAiText[role] = text;
                        AgentLog($"[AI RECV] роль={role} текст={Truncate(text, 120)}");
                        if (lastSentText.TryGetValue(role, out var sentEcho) && sentEcho == text)
                        {
                            AgentLog($"[{role}] пропущено эхо пользователя");
                        }
                        else
                        {
                            LogRole(role, $"[AI]: {text}");
                            if (pendingResponses.TryGetValue(role, out var tcs))
                            {
                                tcs.TrySetResult(text);
                                PlayNotifySound();
                                pendingResponses.TryRemove(role, out _);
                            }
                            else
                            {
                                AgentLog($"[AI ORPHAN] роль={role} ответ пришёл, но никто не ждал");
                            }
                        }
                    }
                    else if (msg.StartsWith("DIAG:"))
                    {
                        var parts = msg.Substring(5).Split('|', 2);
                        var cid = parts[0];
                        var payload = parts.Length > 1 ? parts[1] : "";
                        lastExtDiag[cid] = payload;
                    }
                    else if (msg.StartsWith("TOKEN:"))
                    {
                        config.Token = msg.Substring(6);
                        SaveConfig();
                        AgentLog("[TOKEN ОБНОВЛЕН]");
                    }
                    else if (msg.StartsWith("CHATID:"))
                    {
                        var chatId = msg.Substring(7);
                        AgentLog($"[CHAT ID]: {chatId}");
                    }
                    else if (msg.StartsWith("ROLE:"))
                    {
                        var parts = msg.Substring(5).Split('|', 2);
                        if (parts.Length == 2)
                        {
                            var chatId = parts[0];
                            var role = parts[1];
                            chatRoleMap[chatId] = role;
                            roleChatMap[role] = chatId;
                            if (config.Roles.ContainsKey(role))
                            {
                                config.Roles[role].ChatId = chatId;
                                config.Roles[role].Url = $"https://chat.qwen.ai/c/{chatId}";
                                SaveConfig();
                            }
                            AgentLog($"[ROLE] {role} → {chatId}");
                        }
                    }
                    else if (msg.StartsWith("CAPTCHA:"))
                    {
                        var chatId = msg.Substring(8);
                        AgentLog($"[CAPTCHA] обнаружена в чате {chatId}");
                        ShowBrowserForCaptcha();
                    }
                    else
                    {
                        AgentLog($"[LERON <- EXT] {msg}");
                    }
                }
                clients.Remove(webSocket);
            }
            else
            {
                await next();
            }
        });

        app.MapGet("/status", () => Results.Ok(new
        {
            status = "LERON CLI работает",
            rolesWithChats = roleChatMap.Count,
            roles = roleChatMap.Keys.ToArray()
        }));

        app.MapPost("/send-and-wait", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<SendRequest>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (req == null || string.IsNullOrEmpty(req.Role))
                return Results.BadRequest(new { error = "role is required" });
            var result = await SendToBrowserAndWait(req.Role, req.Text, req.Think, 120000, ctx.RequestAborted);
            if (!result.ok)
                return Results.BadRequest(new { error = result.text });
            return Results.Ok(new { role = req.Role, response = result.text });
        });

        AgentLog("LERON CLI gateway запущен in-process на http://localhost:51234");
        app.Run();
    }
}

class Config
{
    public Dictionary<string, RoleConfig> Roles { get; set; } = new();
    public string Token { get; set; } = "";
    public int RetryAttempts { get; set; } = 3;
    public List<ProjectConfig> Projects { get; set; } = new();
    public List<string> AutoApprove { get; set; } = new();
}

class ProjectConfig
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime? LastOpened { get; set; }
}

class RoleConfig
{
    public string ChatId { get; set; } = "";
    public string Url { get; set; } = "";
}

class SendRequest
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Think { get; set; }
}

class AgentRequest
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Mode { get; set; } = "edit";
    public bool Think { get; set; }
}

class ApproveRequest
{
    public string SessionId { get; set; } = "";
    public bool Approve { get; set; }
    public bool Remember { get; set; }
}

class CancelRequest
{
    public string Role { get; set; } = "";
}

class AgentSession
{
    public string Role { get; set; } = "";
    public string? Root { get; set; }
    public string Mode { get; set; } = "edit";
    public bool Think { get; set; }
    public bool AllowEdit { get; set; }
    public bool UseTools { get; set; } = true;
    public string Backend { get; set; } = "api";
    public string BrowserNextPrompt { get; set; } = "";
    public JsonArray Messages { get; set; } = new();
    public List<string> ToolLog { get; set; } = new();
    public Queue<PendingTool> Pending { get; set; } = new();
}

class PendingTool
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public JsonObject Args { get; set; } = new();
}