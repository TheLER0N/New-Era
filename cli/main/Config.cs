// Config.cs — конфиг, утилиты чтения/записи, JSON
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  AI #2 (вторая ссылка / второй токен)
    // ══════════════════════════════════════════════════════════
    static string Token2 = null;
    static string ApiBaseUrl2 = DefaultApiBase;
    static string ChatId2 = null;
    static string Ai2Model = null;

    // ══════════════════════════════════════════════════════════
    //  SYSTEM_GUARDIAN (двухуровневое редактирование)
    // ══════════════════════════════════════════════════════════
    static bool GuardianEnabled = false;
    static bool ArcMode = false;
    static string GuardianModel = null;
    static string GuardianApiUrl = null;
    static string GuardianToken = null;

    // ══════════════════════════════════════════════════════════
    //  JSON HELPERS
    // ══════════════════════════════════════════════════════════
    static string EscapeJson(string s)
    {
        if (s == null) return "\"\"";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }

    static string JsonStr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "null";
        return EscapeJson(s);
    }

    // ══════════════════════════════════════════════════════════
    //  READ / CONFIG
    // ══════════════════════════════════════════════════════════
    static string ReadTextAuto(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (!File.Exists(path)) return "";
        byte[] raw;
        try { raw = File.ReadAllBytes(path); }
        catch { return ""; }
        if (raw == null || raw.Length == 0) return "";
        Encoding enc;
        int skip = 0;
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        { enc = Encoding.UTF8; skip = 3; }
        else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        { enc = Encoding.Unicode; skip = 2; }
        else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        { enc = Encoding.BigEndianUnicode; skip = 2; }
        else if (raw.Length >= 2 && raw[0] != 0 && raw[1] == 0)
        { enc = Encoding.Unicode; }
        else if (raw.Length >= 2 && raw[0] == 0 && raw[1] != 0)
        { enc = Encoding.BigEndianUnicode; }
        else { enc = Encoding.UTF8; }
        string s;
        try { s = enc.GetString(raw, skip, raw.Length - skip); }
        catch { s = Encoding.UTF8.GetString(raw, skip, raw.Length - skip); }
        if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
        return s.Replace("\0", "");
    }

    static void LoadConfig()
    {
        if (!File.Exists(ConfigFile)) return;
        try
        {
            string[] lines = ReadTextAuto(ConfigFile).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                string t = line.Trim();

                // ── AI #1 ──
                if (t.StartsWith("CHAT_ID=") && string.IsNullOrEmpty(ChatId))
                    ChatId = ExtractChatId(t.Substring(8).Trim());
                else if (t.StartsWith("TOKEN=") && string.IsNullOrEmpty(Token))
                    Token = t.Substring(6).Trim();
                else if (t.StartsWith("API_URL=") && ApiBaseUrl == DefaultApiBase)
                {
                    string url = t.Substring(8).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) ApiBaseUrl = url;
                }
                else if (t.StartsWith("COOKIE=") && string.IsNullOrEmpty(CookieHeader))
                    CookieHeader = t.Substring(7).Trim();

                // ── AI #2 ──
                else if (t.StartsWith("AI2_LINK="))
                {
                    string url = t.Substring(9).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://"))
                    {
                        string tmpBase = ApiBaseUrl2;
                        string tmpId = ChatId2;
                        ParseChatLink(url, ref tmpBase, ref tmpId);
                        ApiBaseUrl2 = tmpBase;
                        if (!string.IsNullOrEmpty(tmpId)) ChatId2 = tmpId;
                    }
                }
                else if (t.StartsWith("AI2_CHAT_ID=") && string.IsNullOrEmpty(ChatId2))
                    ChatId2 = ExtractChatId(t.Substring(12).Trim());
                else if (t.StartsWith("AI2_TOKEN=") && string.IsNullOrEmpty(Token2))
                    Token2 = t.Substring(10).Trim();
                else if (t.StartsWith("AI2_API_URL=") && ApiBaseUrl2 == DefaultApiBase)
                {
                    string url = t.Substring(12).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) ApiBaseUrl2 = url;
                }
                else if (t.StartsWith("AI2_MODEL=") && string.IsNullOrEmpty(Ai2Model))
                    Ai2Model = t.Substring(10).Trim();

                // ── Orchestrator (Dual-LLM) ──
                else if (t.StartsWith("ORCH_ENABLED="))
                {
                    string val = t.Substring(13).Trim().ToLowerInvariant();
                    OrchestratorEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("ORCH_LINK="))
                {
                    string url = t.Substring(10).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://"))
                    {
                        string tmpBase = OrchestratorApiUrl;
                        string tmpId = OrchestratorChatId;
                        ParseChatLink(url, ref tmpBase, ref tmpId);
                        OrchestratorApiUrl = tmpBase;
                        if (!string.IsNullOrEmpty(tmpId)) OrchestratorChatId = tmpId;
                    }
                }
                else if (t.StartsWith("ORCH_CHAT_ID=") && string.IsNullOrEmpty(OrchestratorChatId))
                    OrchestratorChatId = ExtractChatId(t.Substring(13).Trim());
                else if (t.StartsWith("ORCH_MODEL=") && string.IsNullOrEmpty(OrchestratorModel))
                {
                    string val = t.Substring(11).Trim();
                    if (val.Length > 0) OrchestratorModel = val;
                }
                else if (t.StartsWith("ORCH_API_URL=") && string.IsNullOrEmpty(OrchestratorApiUrl))
                {
                    string url = t.Substring(13).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) OrchestratorApiUrl = url;
                }
                else if (t.StartsWith("ORCH_TOKEN=") && string.IsNullOrEmpty(OrchestratorToken))
                {
                    string val = t.Substring(11).Trim();
                    if (val.Length > 0) OrchestratorToken = val;
                }

                // ── SYSTEM_GUARDIAN ──
                else if (t.StartsWith("GUARDIAN_ENABLED="))
                {
                    string val = t.Substring(17).Trim().ToLowerInvariant();
                    GuardianEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("ARC_MODE="))
                {
                    string val = t.Substring(9).Trim().ToLowerInvariant();
                    ArcMode = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("GUARDIAN_MODEL=") && string.IsNullOrEmpty(GuardianModel))
                {
                    string val = t.Substring(15).Trim();
                    if (val.Length > 0) GuardianModel = val;
                }
                else if (t.StartsWith("GUARDIAN_API_URL=") && string.IsNullOrEmpty(GuardianApiUrl))
                {
                    string url = t.Substring(17).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) GuardianApiUrl = url;
                }
                else if (t.StartsWith("GUARDIAN_TOKEN=") && string.IsNullOrEmpty(GuardianToken))
                {
                    string val = t.Substring(15).Trim();
                    if (val.Length > 0) GuardianToken = val;
                }

                // ── Dispatcher v6.0 ──
                else if (t.StartsWith("AI2_DISPATCHER="))
                {
                    string val = t.Substring(15).Trim().ToLowerInvariant();
                    DispatcherEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("AI2_COMPRESS="))
                {
                    string val = t.Substring(13).Trim().ToLowerInvariant();
                    CompressEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("AI2_EXTRACT="))
                {
                    string val = t.Substring(12).Trim().ToLowerInvariant();
                    ExtractEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
            }
        }
        catch { }
    }

    static string ExtractChatId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        if (Regex.IsMatch(input, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))
            return input;
        Match m = Regex.Match(input, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return m.Success ? m.Groups[1].Value : input;
    }

    static void ParseChatLink(string link, ref string baseUrl, ref string chatId)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        link = link.Trim();
        string extractedId = ExtractChatId(link);
        if (!string.IsNullOrEmpty(extractedId) && extractedId != link)
            chatId = extractedId;
        int cIdx = link.IndexOf("/c/", StringComparison.OrdinalIgnoreCase);
        if (cIdx > 0)
            baseUrl = link.Substring(0, cIdx).TrimEnd('/');
        else
        {
            if (link.StartsWith("http://") || link.StartsWith("https://"))
                baseUrl = link.TrimEnd('/');
        }
    }
}