using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MainApp;

public class ChatMessage
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public string Bg { get; set; } = "";
    public string Time { get; set; } = "";
    public string? CardsJson { get; set; }
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
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<ChatMessage>>>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
                JsonSerializer.Serialize(map,
                    new JsonSerializerOptions { WriteIndented = true }));
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