using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MainApp;

internal sealed partial class GatewayState
{
    public Config Config;
    public string ConfigPath;
    public string SrDir;
    public string LogsDir;
    public List<WebSocket> Clients = new();
    public Dictionary<string, string> ChatRoleMap = new();
    public Dictionary<string, string> RoleChatMap = new();
    public ConcurrentDictionary<string, TaskCompletionSource<string>> PendingResponses = new();
    public ConcurrentDictionary<string, CancellationTokenSource> PendingCancels = new();
    public ConcurrentDictionary<string, SemaphoreSlim> RoleSendLocks = new();
    public ConcurrentDictionary<string, CancellationTokenSource> RoleLoopCts = new();
    public ConcurrentDictionary<string, string> LastSentText = new();
    public ConcurrentDictionary<string, byte[]> LastTypePayload = new();
    public ConcurrentDictionary<string, string> LastAiText = new();
    public ConcurrentDictionary<string, string> LastExtDiag = new();
    public ConcurrentDictionary<string, AgentSession> AgentSessions = new();
    // Привязка ответа к запросу: ожидаемый id на роль.
    public ConcurrentDictionary<string, string> ExpectedReqId = new();
    // Буфер осиротевших ответов: ответ пришёл, когда никто не ждал.
    // Храним последний такой ответ на роль, чтобы подхватить его в ожидании.
    public ConcurrentDictionary<string, (string reqId, string text)> OrphanResponses = new();
    private int _reqCounter;
    public string NextReqId() => Interlocked.Increment(ref _reqCounter).ToString();
    public object AutoLock = new();
    public HashSet<string> AutoApproved;

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public GatewayState()
    {
        SrDir = GatewayHost.FindSendReceivingDir();
        ConfigPath = Path.Combine(SrDir, "config.json");
        if (!File.Exists(ConfigPath))
            ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        try
        {
            Config = JsonSerializer.Deserialize<Config>(
                File.ReadAllText(ConfigPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Config();
        }
        catch
        {
            Config = new Config();
        }
        Config.Roles ??= new Dictionary<string, RoleConfig>();
        Config.Projects ??= new List<ProjectConfig>();
        Config.AutoApprove ??= new List<string>();
        Config.ProjectSettings ??= new Dictionary<string, ProjectSettings>();
        foreach (var role in Config.Roles)
        {
            if (!string.IsNullOrEmpty(role.Value.ChatId))
            {
                ChatRoleMap[role.Value.ChatId] = role.Key;
                RoleChatMap[role.Key] = role.Value.ChatId;
            }
        }
        AutoApproved = new HashSet<string>(Config.AutoApprove, StringComparer.OrdinalIgnoreCase);
        try
        {
            LogsDir = Path.Combine(SrDir, "logs");
            if (!Directory.Exists(LogsDir)) Directory.CreateDirectory(LogsDir);
        }
        catch
        {
            LogsDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(LogsDir);
        }
    }

    public void PlayNotifySound()
    {
        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
    }

    public void ShowBrowserForCaptcha()
    {
        try
        {
            var proc = System.Diagnostics.Process.GetProcessesByName("chrome")
                .Concat(System.Diagnostics.Process.GetProcessesByName("msedge"))
                .FirstOrDefault(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
            if (proc != null)
            {
                ShowWindow(proc.MainWindowHandle, 5);
                SetForegroundWindow(proc.MainWindowHandle);
            }
        }
        catch { }
    }

    public void LogRole(string role, string message)
    {
        try
        {
            var logFile = Path.Combine(LogsDir, $"{role}_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public void AgentLog(string message)
    {
        try
        {
            var logFile = Path.Combine(LogsDir, $"agent_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\n");
        }
        catch { }
    }

    public void SaveConfig()
    {
        try
        {
            Config.AutoApprove = AutoApproved.ToList();
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public CancellationTokenSource BeginLoop(string role, CancellationToken httpAbort)
    {
        if (RoleLoopCts.TryRemove(role, out var old))
        {
            try { old.Cancel(); } catch { }
            try { old.Dispose(); } catch { }
            AgentLog($"[LOOP] роль={role} старый цикл принудительно завершён новой задачей");
        }
        foreach (var key in AgentSessions
            .Where(kv => kv.Value.Role == role)
            .Select(kv => kv.Key)
            .ToList())
        {
            AgentSessions.TryRemove(key, out _);
            AgentLog($"[LOOP] роль={role} старая сессия подтверждения сброшена");
        }
        // хвост прошлой задачи не должен попасть в новый цикл
        OrphanResponses.TryRemove(role, out _);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(httpAbort);
        RoleLoopCts[role] = cts;
        return cts;
    }

    public void EndLoop(string role, CancellationTokenSource cts)
    {
        if (RoleLoopCts.TryGetValue(role, out var cur) && ReferenceEquals(cur, cts))
            RoleLoopCts.TryRemove(role, out _);
    }

    public void CancelRole(string role)
    {
        if (PendingCancels.TryRemove(role, out var cts)) cts.Cancel();
        if (RoleLoopCts.TryRemove(role, out var loop))
        {
            try { loop.Cancel(); } catch { }
        }
    }

    public async Task HandleWsClientAsync(WebSocket webSocket)
    {
        Clients.Add(webSocket);
        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    await ms.WriteAsync(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close) break;
                await DispatchWsMessage(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        catch { }
        Clients.Remove(webSocket);
    }

    private async Task DispatchWsMessage(string msg)
    {
        if (msg.StartsWith("AI:"))
        {
            // формат: AI:{chatId}|{reqid}|{text}
            var parts = msg.Substring(3).Split('|', 3);
            var chatId = parts[0];
            var reqId = parts.Length > 2 ? parts[1] : "";
            var text = parts.Length > 2 ? parts[2] : (parts.Length > 1 ? parts[1] : "");
            HandleAiMessage(chatId, reqId, text);
        }
        // SENDFAIL больше не обрабатываем: повторная отправка промпта плодила
        // дубли сообщений в Qwen и несколько ответов на один запрос.
        // Единственный повтор — внутри браузерной панели при SENDRES:NOT_SENT.
        else if (msg.StartsWith("DIAG:"))
        {
            var parts = msg.Substring(5).Split('|', 2);
            LastExtDiag[parts[0]] = parts.Length > 1 ? parts[1] : "";
        }
        else if (msg.StartsWith("TOKEN:"))
        {
            AgentLog("[TOKEN] получен, но прямой API больше не используется");
        }
        else if (msg.StartsWith("CHATID:"))
        {
            AgentLog($"[CHAT ID]: {msg.Substring(7)}");
        }
        else if (msg.StartsWith("ROLE:"))
        {
            var parts = msg.Substring(5).Split('|', 2);
            if (parts.Length == 2)
            {
                var chatId = parts[0];
                var role = parts[1];
                ChatRoleMap[chatId] = role;
                RoleChatMap[role] = chatId;
                if (Config.Roles.ContainsKey(role))
                {
                    Config.Roles[role].ChatId = chatId;
                    Config.Roles[role].Url = $"https://chat.qwen.ai/c/{chatId}";
                    SaveConfig();
                }
                AgentLog($"[ROLE] {role} → {chatId}");
            }
        }
        else if (msg.StartsWith("CAPTCHA:"))
        {
            AgentLog($"[CAPTCHA] обнаружена в чате {msg.Substring(8)}");
            ShowBrowserForCaptcha();
        }
        else
        {
            AgentLog($"[LERON <- EXT] {msg}");
        }
    }

    public void HandleAiMessage(string chatId, string reqId, string text)
    {
        var role = ChatRoleMap.ContainsKey(chatId) ? ChatRoleMap[chatId] : "unknown";
        LastAiText[role] = text;
        AgentLog($"[AI RECV] роль={role} reqid={reqId} текст={Truncate(text, 120)}");

        if (LastSentText.TryGetValue(role, out var sentEcho) && sentEcho == text)
        {
            AgentLog($"[{role}] пропущено эхо пользователя");
            return;
        }

        // ответ со старым id (хвост прошлого запроса) — отбрасываем, не даём закрыть чужое ожидание
        if (!string.IsNullOrEmpty(reqId) &&
            ExpectedReqId.TryGetValue(role, out var expected) &&
            expected != reqId)
        {
            AgentLog($"[AI STALE] роль={role} ответ от запроса {reqId}, ждали {expected} — отброшен");
            return;
        }

        LogRole(role, $"[AI]: {text}");

        if (PendingResponses.TryGetValue(role, out var tcs))
        {
            tcs.TrySetResult(text);
            PlayNotifySound();
            PendingResponses.TryRemove(role, out _);
        }
        else
        {
            // никто не ждал — сохраняем в буфер, ожидание подхватит его по reqId
            OrphanResponses[role] = (reqId, text);
            AgentLog($"[ORPHAN] роль={role} ответ сохранён в буфер (подхватится ожиданием)");
        }
    }

    public ProjectSettings GetProjectSettings(string root)
    {
        var key = NormPath(root).ToLowerInvariant();
        if (!Config.ProjectSettings.TryGetValue(key, out var settings))
        {
            settings = new ProjectSettings();
            Config.ProjectSettings[key] = settings;
        }
        return settings;
    }

    private IEnumerable<string> EnumProjectFiles(string root, string pattern)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> dirs;
            IEnumerable<string> files;
            try
            {
                dirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir, pattern);
            }
            catch { continue; }
            foreach (var d in dirs)
            {
                if (SkipDir(Path.GetFileName(d))) continue;
                stack.Push(d);
            }
            foreach (var f in files) yield return f;
        }
    }

    private string? DetectCheckCommand(string root)
    {
        try
        {
            foreach (var _ in EnumProjectFiles(root, "*.sln")) return "dotnet build";
            foreach (var _ in EnumProjectFiles(root, "*.csproj")) return "dotnet build";
            foreach (var f in EnumProjectFiles(root, "package.json"))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(f));
                    if (node?["scripts"]?["test"] != null) return "npm test";
                    return "npm run build";
                }
                catch { }
            }
            // Python: pyproject.toml / requirements.txt / test_*.py → pytest
            foreach (var _ in EnumProjectFiles(root, "pyproject.toml")) return "pytest";
            foreach (var _ in EnumProjectFiles(root, "requirements.txt")) return "pytest";
            foreach (var _ in EnumProjectFiles(root, "test_*.py")) return "pytest";
        }
        catch { }
        return null;
    }

    public string? EnsureCheckCommand(string root)
    {
        var settings = GetProjectSettings(root);
        // уже найденная команда никогда не перезаписывается (в т.ч. пустотой)
        if (!string.IsNullOrWhiteSpace(settings.CheckCommand)) return settings.CheckCommand;
        var detected = DetectCheckCommand(root);
        if (!string.IsNullOrWhiteSpace(detected))
        {
            settings.CheckCommand = detected;
            SaveConfig();
        }
        // не определена — не выдумываем, проверки просто не будет
        return detected;
    }

    public void AddAutoRule(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        lock (AutoLock)
        {
            if (AutoApproved.Add(rule))
            {
                SaveConfig();
                AgentLog($"[AUTO] добавлено правило: {rule}");
            }
        }
    }

    public bool IsAutoApproved(string rule)
    {
        lock (AutoLock)
        {
            foreach (var approved in AutoApproved)
            {
                if (string.Equals(approved, rule, StringComparison.OrdinalIgnoreCase)) return true;
                if (!rule.StartsWith("run_command:", StringComparison.OrdinalIgnoreCase) &&
                    rule.StartsWith(approved.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public bool HasGrant(AgentSession s, string fullPath, string action)
    {
        foreach (var grant in s.OutsideGrants)
        {
            // Точное совпадение или путь ВНУТРИ гранта: грант на C:\foo
            // не должен разрешать C:\foobar.
            var g = grant.Path.TrimEnd(Path.DirectorySeparatorChar, '/');
            bool inside =
                string.Equals(fullPath, g, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(g + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!inside) continue;
            if (grant.Actions.Contains(action, StringComparer.OrdinalIgnoreCase) ||
                grant.Actions.Contains("all", StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public string? ResolveSessionPath(AgentSession s, string? raw, string action)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var trimmed = raw.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            // Явный запрет ".." ещё до нормализации: путь не имеет права покидать корень.
            if (trimmed.Split(Path.DirectorySeparatorChar).Any(p => p == "..")) return null;
            string full = Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(s.Root ?? Directory.GetCurrentDirectory(), trimmed));
            if (s.Root != null)
            {
                var rootFull = Path.GetFullPath(s.Root);
                if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return full;
            }
            if (HasGrant(s, full, action)) return full;
            return null;
        }
        catch { return null; }
    }

    public string DisplayPath(AgentSession s, string fullPath)
    {
        try
        {
            if (s.Root != null)
            {
                var rootFull = Path.GetFullPath(s.Root);
                if (fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return Path.GetRelativePath(rootFull, fullPath).Replace('\\', '/');
            }
        }
        catch { }
        return fullPath;
    }
}