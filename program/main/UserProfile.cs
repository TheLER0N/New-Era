using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MainApp;

/// <summary>
/// Единый профиль пользователя.
/// Хранилище — секция "HubSettings" в config.json: { "Username", "Description" }.
/// Все читатели (приветствие, футер хаба, страница настроек) берут данные только отсюда.
/// Поддерживает миграцию со старого "UserProfile.Nick/About".
/// </summary>
public static class UserProfile
{
    public static string Nick { get; private set; } = "";
    public static string Description { get; private set; } = "";

    /// <summary>Алиас для обратной совместимости со старым "About".</summary>
    public static string About
    {
        get => Description;
        set => Description = value ?? "";
    }

    public static bool Exists()
    {
        try
        {
            var path = BrowserLauncher.GetConfigPath();
            if (path == null || !File.Exists(path)) return false;
            var node = JsonNode.Parse(File.ReadAllText(path));

            // Новый формат (HubSettings) имеет приоритет, старый (UserProfile) — для миграции.
            var hs = node?["HubSettings"];
            var up = node?["UserProfile"];

            Nick = hs?["Username"]?.GetValue<string>()
                ?? up?["Nick"]?.GetValue<string>()
                ?? "";

            Description = hs?["Description"]?.GetValue<string>()
                ?? up?["About"]?.GetValue<string>()
                ?? "";

            return !string.IsNullOrWhiteSpace(Nick);
        }
        catch
        {
            return false;
        }
    }

    public static void Save(string nick, string description)
    {
        Nick = nick ?? "";
        Description = description ?? "";

        try
        {
            var path = BrowserLauncher.GetConfigPath();
            if (path == null) return;

            JsonObject node;
            try { node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject(); }
            catch { node = new JsonObject(); }

            node["HubSettings"] = new JsonObject
            {
                ["Username"] = Nick,
                ["Description"] = Description
            };

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(
                path,
                node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Gateway.GuiTestLogger.Log("PROFILE_SAVE", nick, "ошибка: " + ex.Message, false);
        }
    }
}