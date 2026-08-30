using System;
using System.Linq;

namespace MainApp;

internal sealed partial class GatewayState
{
    internal static string? LastBrowserChatId;

    // Автопривязка роли: последний известный chatId браузера, иначе sentinel "current"
    // (панель всё равно печатает в открытый чат и эхоит chatId обратно).
    internal void TryAutoBind(string role)
    {
        try
        {
            if (string.IsNullOrEmpty(role)) return;
            if (RoleChatMap.TryGetValue(role, out var ex) && !string.IsNullOrEmpty(ex)) return;
            var cid = ChatRoleMap.Keys.LastOrDefault(k => !string.IsNullOrEmpty(k)) ?? LastBrowserChatId ?? "current";
            RoleChatMap[role] = cid;
            ChatRoleMap[cid] = role;
        }
        catch { }
    }
}
