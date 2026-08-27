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
    public ConcurrentDictionary<string, string> ExpectedReqId = new();
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
        catch { Config = new Config(); }
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
            JsonObject node;
            try
            {
                if (File.Exists(ConfigPath))
                    node = JsonNode.Parse(File.ReadAllText(ConfigPath))?.AsObject() ?? new JsonObject();
                else
                    node = new JsonObject();
            }
            catch { node = new JsonObject(); }
            var opts = new JsonSerializerOptions { WriteIndented = true };
            node["Roles"] = JsonSerializer.SerializeToNode(Config.Roles, opts);
            node["RetryAttempts"] = Config.RetryAttempts;
            node["Projects"] = JsonSerializer.SerializeToNode(Config.Projects, opts);
            node["AutoApprove"] = JsonSerializer.SerializeToNode(Config.AutoApprove, opts);
            node["ProjectSettings"] = JsonSerializer.SerializeToNode(Config.ProjectSettings, opts);
            File.WriteAllText(ConfigPath, node.ToJsonString(opts));
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
            var parts = msg.Substring(3).Split('|', 3);
            var chatId = parts[0];
            var reqId = parts.Length > 2 ? parts[1] : "";
            var text = parts.Length > 2 ? parts[2] : (parts.Length > 1 ? parts[1] : "");
            HandleAiMessage(chatId, reqId, text);
        }
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
        if (!string.IsNullOrWhiteSpace(settings.CheckCommand)) return settings.CheckCommand;
        var detected = DetectCheckCommand(root);
        if (!string.IsNullOrWhiteSpace(detected))
        {
            settings.CheckCommand = detected;
            SaveConfig();
        }
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
    // ── Дерево проекта для первого промпта (до 300 строк) ──
    public string GetProjectTree(string root, int maxLines = 300)
    {
        var sb = new StringBuilder();
        int count = 0;
        void Walk(string dir, string indent, bool isLast)
        {
            if (count >= maxLines) return;
            IEnumerable<string> dirs, files;
            try
            {
                dirs = Directory.EnumerateDirectories(dir)
                    .Where(d => !SkipDir(Path.GetFileName(d)))
                    .OrderBy(d => d);
                files = Directory.EnumerateFiles(dir)
                    .Where(f => !IsBinaryExt(f))
                    .OrderBy(f => f);
            }
            catch { return; }
            var entries = dirs.Cast<string>().Concat(files).ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                if (count >= maxLines) break;
                var e = entries[i];
                var last = i == entries.Count - 1;
                var name = Path.GetFileName(e);
                var connector = last ? "└── " : "├── ";
                sb.AppendLine(indent + connector + name);
                count++;
                if (Directory.Exists(e))
                    Walk(e, indent + (last ? "    " : "│   "), last);
            }
        }
        sb.AppendLine(root);
        Walk(root, "", true);
        if (count >= maxLines)
        {
            sb.AppendLine($"… и ещё файлов — используй read_files/list_files для детального просмотра");
        }
        return sb.ToString();
    }
    // ── Проверка кэша прочитанных файлов ──
    public (bool cached, string message) CheckReadCache(AgentSession s, string fullPath, bool repairMode)
    {
        if (repairMode) return (false, "");
        if (s.SelfModified.Contains(fullPath))
            return (true, "содержимое известно — ты сам записал этот файл в этой сессии");
        if (!File.Exists(fullPath)) return (false, "");
        try
        {
            var fi = new FileInfo(fullPath);
            if (s.ReadCache.TryGetValue(fullPath, out var cached))
            {
                if (cached.MTime == fi.LastWriteTime && cached.Length == fi.Length)
                    return (true, $"файл не менялся с последнего чтения (mtime={cached.MTime:HH:mm:ss}, size={cached.Length})");
            }
        }
        catch { }
        return (false, "");
    }
    public void UpdateReadCache(AgentSession s, string fullPath)
    {
        try
        {
            var fi = new FileInfo(fullPath);
            s.ReadCache[fullPath] = (fi.LastWriteTime, fi.Length);
        }
        catch { }
    }
    // ── Раунд 3: индекс файлов .leron/file_index.json ───────────────
    // Путь индекса в корне проекта; кэш по корню, чтобы не читать диск каждый шаг.
    public static string FileIndexPath(string root) =>
        Path.Combine(root, ".leron", "file_index.json");
    private readonly Dictionary<string, FileIndex> _fileIndexes =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions FileIndexJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    public FileIndex GetFileIndex(string root)
    {
        var key = NormPath(root).ToLowerInvariant();
        if (_fileIndexes.TryGetValue(key, out var cached)) return cached;
        var index = new FileIndex();
        try
        {
            var path = FileIndexPath(root);
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<FileIndex>(
                    File.ReadAllText(path), FileIndexJsonOpts);
                if (loaded?.Files != null) index = loaded;
            }
        }
        catch { }
        _fileIndexes[key] = index;
        return index;
    }
    public void SaveFileIndex(string root)
    {
        var key = NormPath(root).ToLowerInvariant();
        if (!_fileIndexes.TryGetValue(key, out var index)) return;
        try
        {
            var path = FileIndexPath(root);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(index, FileIndexJsonOpts));
            AgentLog($"[INDEX] сохранён {path}: описаний={index.Files.Count}");
        }
        catch { }
    }
    // Блок «КРАТКИЕ ОПИСАНИЯ ФАЙЛОВ» для промпта: описание + отметка свежести.
    // Если файл изменили после записи описания (mtime/size не совпадают) —
    // помечаем, чтобы ИИ перечитал файл перед правкой.
    public string GetFileIndexPrompt(AgentSession s)
    {
        if (s.Root == null) return "";
        var index = GetFileIndex(s.Root);
        if (index.Files.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("=== КРАТКИЕ ОПИСАНИЯ ФАЙЛОВ ===");
        foreach (var kv in index.Files.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rel = kv.Key;
            var e = kv.Value;
            var mark = "";
            try
            {
                var full = Path.GetFullPath(Path.Combine(s.Root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(full)) mark = " ⚠ файл не найден";
                else if (e.MTime != null)
                {
                    var fi = new FileInfo(full);
                    if (fi.LastWriteTime != e.MTime.Value || fi.Length != e.Size)
                        mark = " ⚠ файл изменён после этого описания — прочитай перед правкой";
                }
            }
            catch { }
            sb.AppendLine($"- {rel}: {e.Summary}{mark}");
        }
        sb.AppendLine("После ЛЮБОГО изменения файла вызови update_file_summaries с новым описанием.");
        return sb.ToString();
    }
}