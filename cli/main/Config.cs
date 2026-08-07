// Config.cs — загрузка/сохранение конфигурации, AI#2 хелперы + Build Gate ключи
// New Era v7.2+
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

partial class MainConsole
{
    // Build Gate global config
    static bool BuildAfterEdit = true;
    static bool GlobalAutoRepair = true;
    static string DefaultVerifyMode = "smoke";
    static string DefaultReportDir = ".newera/reports";
    static int MaxDiskRollbacks = 50;

    static void LoadConfig()
    {
        if (!File.Exists(ConfigFile)) return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                string[] lines = ReadTextAuto(ConfigFile).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("#")) continue;

                    if (t.StartsWith("MODEL=")) { string v = t.Substring(6).Trim(); if (v.Length > 0) PrimaryModel = v; }
                    else if (t.StartsWith("CHAT_ID=") && string.IsNullOrEmpty(ChatId)) ChatId = ExtractChatId(t.Substring(8).Trim());
                    else if (t.StartsWith("TOKEN=") && string.IsNullOrEmpty(Token)) Token = t.Substring(6).Trim();
                    else if (t.StartsWith("API_URL=") && ApiBaseUrl == DefaultApiBase)
                    {
                        string url = t.Substring(8).Trim();
                        if (url.StartsWith("http://") || url.StartsWith("https://")) ApiBaseUrl = url;
                    }
                    else if (t.StartsWith("COOKIE=") && string.IsNullOrEmpty(CookieHeader)) CookieHeader = t.Substring(7).Trim();

                    else if (t.StartsWith("AI2_TOKEN=") && string.IsNullOrEmpty(Token2)) Token2 = t.Substring(10).Trim();
                    else if (t.StartsWith("AI2_API_URL=") && ApiBaseUrl2 == DefaultApiBase)
                    {
                        string url = t.Substring(12).Trim();
                        if (url.StartsWith("http://") || url.StartsWith("https://")) ApiBaseUrl2 = url;
                    }
                    else if (t.StartsWith("AI2_LINK="))
                    {
                        string url = t.Substring(9).Trim();
                        if (url.StartsWith("http://") || url.StartsWith("https://"))
                        {
                            string tmpBase = ApiBaseUrl2, tmpId = ChatId2;
                            ParseChatLink(url, ref tmpBase, ref tmpId);
                            ApiBaseUrl2 = tmpBase;
                            if (!string.IsNullOrEmpty(tmpId)) ChatId2 = tmpId;
                        }
                    }
                    else if (t.StartsWith("AI2_CHAT_ID=") && string.IsNullOrEmpty(ChatId2)) ChatId2 = ExtractChatId(t.Substring(12).Trim());
                    else if (t.StartsWith("QWEN_VERSION=")) { string v = t.Substring(13).Trim(); if (v.Length > 0) QwenVersion = v; }
                    else if (t.StartsWith("AI2_MODEL=")) { string v = t.Substring(10).Trim(); if (v.Length > 0) Ai2Model = v; }

                    else if (t.StartsWith("AI2_DISPATCHER=")) DispatcherEnabled = ParseBool(t.Substring(15));
                    else if (t.StartsWith("AI2_COMPRESS=")) CompressEnabled = ParseBool(t.Substring(13));
                    else if (t.StartsWith("AI2_EXTRACT=")) ExtractEnabled = ParseBool(t.Substring(12));
                    else if (t.StartsWith("AI2_VALIDATE=")) Ai2ValidateEnabled = ParseBool(t.Substring(13));
                    else if (t.StartsWith("PROJECT_PATH=")) { string v = t.Substring(13).Trim(); if (v.Length > 0) ProjectPath = v; }
                    else if (t.StartsWith("ARC_MODE=")) ArcMode = ParseBool(t.Substring(9));

                    else if (t.StartsWith("MAX_CONTEXT_TOTAL=")) MaxContextTotal = ParseInt(t.Substring(18), MaxContextTotal);
                    else if (t.StartsWith("MAX_CONTEXT_FILE=")) MaxContextFile = ParseInt(t.Substring(17), MaxContextFile);
                    else if (t.StartsWith("MAX_HISTORY_ENTRIES=")) MaxHistoryEntries = ParseInt(t.Substring(19), MaxHistoryEntries);
                    else if (t.StartsWith("PLAN_MAX_RETRIES=")) PlanMaxRetries = ParseInt(t.Substring(17), PlanMaxRetries);
                    else if (t.StartsWith("PLAN_RETRY_DELAY_MS=")) PlanRetryDelayMs = ParseInt(t.Substring(20), PlanRetryDelayMs);

                    // Build Gate
                    else if (t.StartsWith("BUILD_AFTER_EDIT=")) BuildAfterEdit = ParseBool(t.Substring(17));
                    else if (t.StartsWith("AUTO_REPAIR=")) GlobalAutoRepair = ParseBool(t.Substring(12));
                    else if (t.StartsWith("VERIFY_MODE=")) { string v = t.Substring(12).Trim(); if (v.Length > 0) DefaultVerifyMode = v; }
                    else if (t.StartsWith("REPORT_DIR=")) { string v = t.Substring(11).Trim(); if (v.Length > 0) DefaultReportDir = v; }
                    else if (t.StartsWith("MAX_DISK_ROLLBACKS=")) MaxDiskRollbacks = ParseInt(t.Substring(19), MaxDiskRollbacks);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200 * (attempt + 1));
            }
            catch
            {
                return;
            }
        }
    }

    // ══════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════

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
        if (!string.IsNullOrEmpty(extractedId) && extractedId != link) chatId = extractedId;

        int cIdx = link.IndexOf("/c/", StringComparison.OrdinalIgnoreCase);
        if (cIdx > 0) baseUrl = link.Substring(0, cIdx).TrimEnd('/');
        else if (link.StartsWith("http://") || link.StartsWith("https://")) baseUrl = link.TrimEnd('/');
    }

    static int ParseInt(string s, int fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        int v;
        if (int.TryParse(s.Trim(), out v)) return v;
        return fallback;
    }

    static bool ParseBool(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string t = s.Trim().ToLowerInvariant();
        return t == "1" || t == "true" || t == "yes" || t == "on" || t == "y";
    }

    // ══════════════════════════════════════════════
    //  AI #2 HELPERS
    // ══════════════════════════════════════════════

    static string GetAi2Token() { return Token2; }

    static string GetAi2Api()
    {
        return (!string.IsNullOrEmpty(ApiBaseUrl2) && ApiBaseUrl2 != DefaultApiBase) ? ApiBaseUrl2 : ApiBaseUrl;
    }

    static string GetAi2Model()
    {
        return !string.IsNullOrEmpty(Ai2Model) ? Ai2Model : DefaultAi2Model;
    }

    static bool IsAi2Configured()
    {
        return !string.IsNullOrEmpty(Token2) && !string.IsNullOrEmpty(ChatId2);
    }
}