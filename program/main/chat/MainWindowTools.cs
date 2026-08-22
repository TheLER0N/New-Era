using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MainApp;

public partial class MainWindow
{
    private void RunLocalTool(string text)
    {
        var role = _selectedRole!;
        if (_projectPath == null)
        {
            AddMessage(role, "система", "Инструменты доступны только при открытом проекте. Выбери проект в хабе.", "#e94560");
            return;
        }

        var parts = text.Split(new[] { ' ' }, 2);
        var cmd = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1].Trim() : "";

        string result;
        try
        {
            result = cmd switch
            {
                "/list" => ToolList(rest),
                "/read" => ToolRead(rest),
                "/write" => ToolWrite(rest),
                "/delete" => ToolDelete(rest),
                _ => $"Неизвестная команда: {cmd}. Доступны: /list /read /write /delete"
            };
        }
        catch (Exception ex)
        {
            result = "Ошибка: " + ex.Message;
        }

        AddMessage(role, "инструмент", result, "#123020");
    }

    private string? ResolveInProject(string raw)
    {
        var trimmed = raw.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed) || _projectPath == null) return null;
        trimmed = trimmed.Replace('/', Path.DirectorySeparatorChar);

        var rootFull = Path.GetFullPath(_projectPath);
        string full = Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(rootFull, trimmed));

        if (!full.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(rootFull + Path.DirectorySeparatorChar))
            return null;
        return full;
    }

    private string ToolList(string rawPath)
    {
        var p = ResolveInProject(string.IsNullOrWhiteSpace(rawPath) ? "." : rawPath);
        if (p == null) return "Доступ отклонён: путь вне проекта.";
        if (!Directory.Exists(p)) return $"Папка не найдена: {rawPath}";

        var sb = new StringBuilder();
        foreach (var d in Directory.GetDirectories(p).OrderBy(x => x))
            sb.AppendLine("📁 " + Path.GetFileName(d));
        foreach (var f in Directory.GetFiles(p).OrderBy(x => x))
            sb.AppendLine("📄 " + Path.GetFileName(f));

        var list = sb.ToString();
        return string.IsNullOrWhiteSpace(list) ? "(пусто)" : list;
    }

    private string ToolRead(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return "Укажи путь. Пример: /read ./Program.cs";
        var p = ResolveInProject(rawPath);
        if (p == null) return "Доступ отклонён: путь вне проекта.";
        if (!File.Exists(p)) return $"Файл не найден: {rawPath}";

        var text = File.ReadAllText(p);
        if (text.Length > 20000)
            text = text.Substring(0, 20000) + "\n…[обрезано]";
        return $"--- {rawPath} ---\n{text}";
    }

    private string ToolWrite(string rest)
    {
        var wp = rest.Split(new[] { ' ' }, 2);
        if (string.IsNullOrWhiteSpace(wp[0]))
            return "Укажи путь и текст. Пример: /write ./test.txt hello";
        var p = ResolveInProject(wp[0]);
        if (p == null) return "Доступ отклонён: путь вне проекта.";

        var dir = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(p, wp.Length > 1 ? wp[1] : "");
        return $"Файл создан: {wp[0]}";
    }

    private string ToolDelete(string rawPath)
    {
        var p = ResolveInProject(rawPath);
        if (p == null) return "Доступ отклонён: путь вне проекта.";
        if (File.Exists(p)) { File.Delete(p); return $"Файл удалён: {rawPath}"; }
        if (Directory.Exists(p)) { Directory.Delete(p, true); return $"Папка удалена: {rawPath}"; }
        return $"Не найдено: {rawPath}";
    }
}

public class ChatMessage
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public string Bg { get; set; } = "";
}

class GatewayStatus
{
    public string Status { get; set; } = "";
    public int RolesWithChats { get; set; }
    public string[] Roles { get; set; } = [];
}

class SendResponse
{
    public string Role { get; set; } = "";
    public string Response { get; set; } = "";
}

class AgentRunResponse
{
    public string Status { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Role { get; set; } = "";
    public string Response { get; set; } = "";
    public List<string> Tools { get; set; } = new();
    public List<ActionCardDto>? Cards { get; set; }
    public string Tool { get; set; } = "";
    public string Arguments { get; set; } = "";
    public bool Dangerous { get; set; }
    public string Question { get; set; } = "";
    public int RequestedCount { get; set; }
    public string Reason { get; set; } = "";
    public string Path { get; set; } = "";
    public string RequestedActions { get; set; } = "";
    public string ResultStatus { get; set; } = "";
    public List<string>? ChangedFiles { get; set; }
}

class ActionCardDto
{
    public string Type { get; set; } = "";
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

public static class HistoryStore
{
    public static string ProjectKey(string projectPath) =>
        "proj|" + projectPath.TrimEnd('\\', '/').ToLowerInvariant();

    public static string? GetPath()
    {
        var configPath = BrowserLauncher.GetConfigPath();
        if (configPath == null) return null;
        var dir = Path.GetDirectoryName(configPath);
        if (dir == null) return null;
        return Path.Combine(dir, "history.json");
    }

    public static Dictionary<string, List<ChatMessage>> Load()
    {
        try
        {
            var path = GetPath();
            if (path == null || !File.Exists(path)) return new();
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<ChatMessage>>>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return loaded ?? new();
        }
        catch { return new(); }
    }

    public static void Save(Dictionary<string, List<ChatMessage>> map)
    {
        try
        {
            var path = GetPath();
            if (path == null) return;
            File.WriteAllText(path,
                System.Text.Json.JsonSerializer.Serialize(map,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void DeleteProjectHistory(string projectPath)
    {
        var map = Load();
        if (map.Remove(ProjectKey(projectPath)))
            Save(map);
    }
}