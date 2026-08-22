using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MainApp;

internal static class JsonOpts
{
    public static readonly JsonSerializerOptions Ci = new() { PropertyNameCaseInsensitive = true };
}

class Config
{
    public Dictionary<string, RoleConfig> Roles { get; set; } = new();
    public int RetryAttempts { get; set; } = 3;
    public List<ProjectConfig> Projects { get; set; } = new();
    public List<string> AutoApprove { get; set; } = new();
    public Dictionary<string, ProjectSettings> ProjectSettings { get; set; } = new();
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

class ProjectSettings
{
    public string? CheckCommand { get; set; }
    public int? MaxSteps { get; set; }
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
    public int Steps { get; set; }
    public string? InputText { get; set; }
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
    public bool AllowTools { get; set; }
    public string BrowserNextPrompt { get; set; } = "";
    public JsonArray Messages { get; set; } = new();
    public List<string> ToolLog { get; set; } = new();
    public Queue<PendingTool> Pending { get; set; } = new();
    public List<ActionCard> Cards { get; set; } = new();
    public int CardsSent { get; set; }
    public HashSet<string> ChangedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int StepLimit { get; set; } = 8;
    public int StepUsed { get; set; }
    public bool RepairMode { get; set; }
    public int RepairAttempts { get; set; }
    public List<OutsideGrant> OutsideGrants { get; set; } = new();
    public HashSet<string> DangerApproved { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

class PendingTool
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public JsonObject Args { get; set; } = new();
}

class OutsideGrant
{
    public string Path { get; set; } = "";
    public HashSet<string> Actions { get; set; } = new();
}

class ActionCard
{
    public string Type { get; set; } = "info";
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
    public string? Path { get; set; }
    public string? Command { get; set; }
    public string? Shell { get; set; }
    public int? ExitCode { get; set; }
    public int? Count { get; set; }
    public bool Backup { get; set; }
    public string? OldText { get; set; }
    public string? NewText { get; set; }
}

class ToolExecution
{
    public string Tool { get; set; } = "";
    public string Output { get; set; } = "";
    public string Log { get; set; } = "";
    public bool Mutated { get; set; }
    public string? Path { get; set; }
    public ActionCard Card { get; set; } = new();
}

class CommandResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
    public string Output { get; set; } = "";
    public string Shell { get; set; } = "CMD";
    public bool TimedOut { get; set; }
}

// ── чистые статические помощники GatewayState ────────────────
internal sealed partial class GatewayState
{
    public static string NormPath(string path) => path.TrimEnd('\\', '/');

    public static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";

    public static string Tail(string s, int n) =>
        string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(s.Length - n);

    public static string NormCommand(string command) =>
        Regex.Replace(command.Trim().ToLowerInvariant(), @"\s+", " ");

    public static string CommandKey(string command) => $"run_command:{NormCommand(command)}";

    public static bool SkipDir(string name) =>
        name is "bin" or "obj" or ".git" or ".vs" or ".vscode" or ".idea" or ".leron" or "node_modules";

    public static bool IsBinaryExt(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".pdf"
            or ".zip" or ".rar" or ".7z" or ".exe" or ".dll" or ".pdb" or ".bin"
            or ".mp3" or ".mp4" or ".avi" or ".mov" or ".woff" or ".woff2" or ".ttf";
    }

    public static bool ModeAllowsEdit(string mode) =>
        mode is "edit" or "auto" or "yolo" or "repair";

    public static bool IsMutating(string name) =>
        name is "write_file" or "patch_file" or "edit_file" or "rename_file"
            or "delete_file" or "create_directory";

    public static bool IsSpecial(string name) =>
        name is "request_user_input" or "request_more_steps" or "request_outside_access";

    public static bool IsKnownTool(string name) =>
        name is "read_file" or "list_files" or "grep" or "write_file" or "patch_file"
            or "edit_file" or "rename_file" or "delete_file" or "create_directory"
            or "run_command" or "request_user_input" or "request_more_steps"
            or "request_outside_access" or "finish";

    public static bool IsDangerousCommand(string command)
    {
        var cmd = NormCommand(command);
        string[] dangerous =
        {
            "rm -rf", "rmdir /s", "rd /s", "del /s", "erase /s",
            "git push --force", "git push -f", "git reset --hard",
            "format", "diskpart", "shutdown", "drop database", "drop table"
        };
        foreach (var d in dangerous)
            if (cmd.Contains(d)) return true;
        return false;
    }

    public static bool IsDangerousTool(AgentSession s, PendingTool c)
    {
        if (c.Name != "run_command") return false;
        return IsDangerousCommand(GetStr(c.Args, "command"));
    }

    public static string PathRule(string tool, string fullPath, string? root)
    {
        try
        {
            if (root != null)
            {
                var rootFull = Path.GetFullPath(root);
                var full = Path.GetFullPath(fullPath);
                if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
                    string dir = Directory.Exists(full)
                        ? rel
                        : Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
                    dir = dir.Trim('/');
                    return $"{tool}:{(string.IsNullOrEmpty(dir) ? "." : dir)}";
                }
            }
        }
        catch { }
        return $"{tool}:{fullPath.Replace('\\', '/').Trim('/')}";
    }

    public static string StripProviderMetadata(string text)
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

    // Надёжный парсинг вызова инструмента: ищем первый сбалансированный
    // JSON-объект {"name":"...","arguments":{...}} где угодно в тексте
    // (лишний текст, markdown-обёртки). "arguments" может быть объектом
    // либо JSON-строкой. Возвращаем первый объект, который реально является
    // известным инструментом.
    public static (string name, JsonObject args)? TryParseTextToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var candidate in ExtractJsonObjects(text))
        {
            var parsed = TryParseToolObject(candidate);
            if (parsed != null) return parsed;
        }
        return null;
    }

    private static (string name, JsonObject args)? TryParseToolObject(string candidate)
    {
        try
        {
            var node = JsonNode.Parse(candidate) as JsonObject;
            if (node == null) return null;

            var name = GetStr(node, "name");
            if (string.IsNullOrEmpty(name) || !IsKnownTool(name)) return null;

            var args = node["arguments"] as JsonObject;
            if (args == null)
            {
                var argsStr = node["arguments"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(argsStr))
                    args = JsonNode.Parse(argsStr) as JsonObject;
            }
            return (name, args ?? new JsonObject());
        }
        catch { return null; }
    }

    // Все сбалансированные объекты {...} в порядке появления.
    private static IEnumerable<string> ExtractJsonObjects(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            int end = FindJsonObjectEnd(text, i);
            if (end < 0) continue;
            yield return text.Substring(i, end - i + 1);
            i = end; // вложенные объекты уже внутри — пропускаем
        }
    }

    // Индекс закрывающей '}' для объекта, начинающегося в start.
    // Учитывает строки и экранирование, чтобы скобки внутри строк не сбивали счёт.
    private static int FindJsonObjectEnd(string text, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < text.Length; i++)
        {
            char ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }
            if (ch == '"') inString = true;
            else if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    public static string GetStr(JsonObject args, string key, string def = "")
    {
        if (args[key] is JsonNode node)
        {
            try
            {
                if (node.GetValueKind() == JsonValueKind.String)
                    return node.GetValue<string>() ?? def;
                return node.ToJsonString();
            }
            catch { return def; }
        }
        return def;
    }

    public static int GetInt(JsonObject args, string key, int def)
    {
        if (args[key] is JsonNode node)
        {
            try
            {
                if (node.GetValueKind() == JsonValueKind.Number)
                    return node.GetValue<int>();
                if (int.TryParse(node.ToString(), out var parsed))
                    return parsed;
            }
            catch { }
        }
        return def;
    }

    public static bool GetBool(JsonObject args, string key, bool def)
    {
        if (args[key] is JsonNode node)
        {
            try
            {
                var kind = node.GetValueKind();
                if (kind == JsonValueKind.True) return true;
                if (kind == JsonValueKind.False) return false;
                if (bool.TryParse(node.ToString(), out var parsed)) return parsed;
            }
            catch { }
        }
        return def;
    }

    public static ActionCard ErrorCard(string title, string status, string pathOrCommand)
    {
        return new ActionCard
        {
            Type = "error",
            Icon = "⚠️",
            Title = title,
            Status = status,
            Path = pathOrCommand,
            Details = status
        };
    }

    public static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}