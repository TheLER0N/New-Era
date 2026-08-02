// Config.cs — конфиг, утилиты чтения, AI #2, безопасные пути
// New Era CLI v6.0
// C# 5 / .NET Framework 4.x

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  AI #2
    // ══════════════════════════════════════════════════════════
    static string Token2 = null;
    static string ApiBaseUrl2 = DefaultApiBase;
    static string ChatId2 = null;
    static string Ai2Model = null;

    // ══════════════════════════════════════════════════════════
    //  PROJECT / VALIDATION / ARCMODE
    // ══════════════════════════════════════════════════════════
    static string ProjectPath = null;
    static bool Ai2ValidateEnabled = false;
    static bool ArcMode = false;
    const string DefaultAi2Model = "qwen3.7-max";

    // ══════════════════════════════════════════════════════════
    //  JSON HELPERS
    // ══════════════════════════════════════════════════════════
    static string EscapeJson(string s)
    {
        if (s == null)
            return "\"\"";

        var sb = new StringBuilder("\"");

        foreach (char c in s)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;

                case '\\':
                    sb.Append("\\\\");
                    break;

                case '\n':
                    sb.Append("\\n");
                    break;

                case '\r':
                    sb.Append("\\r");
                    break;

                case '\t':
                    sb.Append("\\t");
                    break;

                default:
                    if (c < 0x20)
                        sb.Append("\\u" + ((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append("\"");
        return sb.ToString();
    }

    static string JsonStr(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "null";

        return EscapeJson(s);
    }

    // ══════════════════════════════════════════════════════════
    //  READ TEXT AUTO
    // ══════════════════════════════════════════════════════════
    static string ReadTextAuto(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        if (!File.Exists(path))
            return "";

        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(path);
        }
        catch
        {
            return "";
        }

        if (raw == null || raw.Length == 0)
            return "";

        Encoding enc;
        int skip = 0;

        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            enc = Encoding.UTF8;
            skip = 3;
        }
        else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            enc = Encoding.Unicode;
            skip = 2;
        }
        else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF)
        {
            enc = Encoding.BigEndianUnicode;
            skip = 2;
        }
        else if (raw.Length >= 2 && raw[0] != 0 && raw[1] == 0)
        {
            enc = Encoding.Unicode;
        }
        else if (raw.Length >= 2 && raw[0] == 0 && raw[1] != 0)
        {
            enc = Encoding.BigEndianUnicode;
        }
        else
        {
            enc = Encoding.UTF8;
        }

        string s;
        try
        {
            s = enc.GetString(raw, skip, raw.Length - skip);
        }
        catch
        {
            s = Encoding.UTF8.GetString(raw, skip, raw.Length - skip);
        }

        if (s.Length > 0 && s[0] == '\uFEFF')
            s = s.Substring(1);

        return s.Replace("\0", "");
    }

    // ══════════════════════════════════════════════════════════
    //  LOAD CONFIG
    // ══════════════════════════════════════════════════════════
    static void LoadConfig()
    {
        if (!File.Exists(ConfigFile))
            return;

        try
        {
            string[] lines = ReadTextAuto(ConfigFile)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string line in lines)
            {
                string t = line.Trim();

                // ── Primary ──
                if (t.StartsWith("MODEL="))
                {
                    string val = t.Substring(6).Trim();
                    if (val.Length > 0)
                        PrimaryModel = val;
                }
                else if (t.StartsWith("QWEN_VERSION="))
                {
                    string val = t.Substring(13).Trim();
                    if (val.Length > 0)
                        QwenVersion = val;
                }
                else if (t.StartsWith("CHAT_ID=") && string.IsNullOrEmpty(ChatId))
                {
                    ChatId = ExtractChatId(t.Substring(8).Trim());
                }
                else if (t.StartsWith("TOKEN=") && string.IsNullOrEmpty(Token))
                {
                    Token = t.Substring(6).Trim();
                }
                else if (t.StartsWith("API_URL=") && ApiBaseUrl == DefaultApiBase)
                {
                    string url = t.Substring(8).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://"))
                        ApiBaseUrl = url;
                }
                else if (t.StartsWith("COOKIE=") && string.IsNullOrEmpty(CookieHeader))
                {
                    CookieHeader = t.Substring(7).Trim();
                }

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
                        if (!string.IsNullOrEmpty(tmpId))
                            ChatId2 = tmpId;
                    }
                }
                else if (t.StartsWith("AI2_CHAT_ID=") && string.IsNullOrEmpty(ChatId2))
                {
                    ChatId2 = ExtractChatId(t.Substring(12).Trim());
                }
                else if (t.StartsWith("AI2_TOKEN=") && string.IsNullOrEmpty(Token2))
                {
                    Token2 = t.Substring(10).Trim();
                }
                else if (t.StartsWith("AI2_API_URL=") && ApiBaseUrl2 == DefaultApiBase)
                {
                    string url = t.Substring(12).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://"))
                        ApiBaseUrl2 = url;
                }
                else if (t.StartsWith("AI2_MODEL=") && string.IsNullOrEmpty(Ai2Model))
                {
                    Ai2Model = t.Substring(10).Trim();
                }

                // ── v6.0 dispatcher ──
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
                else if (t.StartsWith("AI2_VALIDATE="))
                {
                    string val = t.Substring(13).Trim().ToLowerInvariant();
                    Ai2ValidateEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("PROJECT_PATH="))
                {
                    string val = t.Substring(13).Trim();
                    if (val.Length > 0)
                        ProjectPath = val;
                }

                // ── ArcMode remains ──
                else if (t.StartsWith("ARC_MODE="))
                {
                    string val = t.Substring(9).Trim().ToLowerInvariant();
                    ArcMode = (val == "1" || val == "true" || val == "on" || val == "yes");
                }

                // Старые ORCH_* и GUARDIAN_* ключи больше не используются.
                // Они читаются как deprecated и игнорируются.
            }
        }
        catch
        {
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PROJECT PATH
    // ══════════════════════════════════════════════════════════
    static string ResolveProjectPath()
    {
        return ResolveProjectDirectory(null);
    }

    static string ResolveProjectDirectory(string preferredPath)
    {
        string candidate = preferredPath;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = ProjectPath;

        string resolved = ResolveCandidateDirectory(candidate);
        if (!string.IsNullOrEmpty(resolved))
            return resolved;

        if (!string.IsNullOrWhiteSpace(ProjectPath) &&
            !string.Equals(ProjectPath, candidate, StringComparison.OrdinalIgnoreCase))
        {
            resolved = ResolveCandidateDirectory(ProjectPath);
            if (!string.IsNullOrEmpty(resolved))
                return resolved;
        }

        try
        {
            string cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
                return cwd;
        }
        catch
        {
        }

        return BaseDir;
    }

    static string ResolveCandidateDirectory(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        try
        {
            string full = Path.GetFullPath(candidate.Trim('"'));

            if (File.Exists(full))
                full = Path.GetDirectoryName(full);

            return GetExistingDirectoryOrProjectRoot(full);
        }
        catch
        {
            return null;
        }
    }

    static string GetExistingDirectoryOrProjectRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var dir = new DirectoryInfo(path);
            string fallback = null;

            while (dir != null)
            {
                if (dir.Exists)
                {
                    if (fallback == null)
                        fallback = dir.FullName;

                    if (LooksLikeProjectRoot(dir.FullName))
                        return dir.FullName;
                }

                dir = dir.Parent;
            }

            return fallback;
        }
        catch
        {
            return null;
        }
    }

    static bool LooksLikeProjectRoot(string dir)
    {
        try
        {
            if (Directory.GetFiles(dir, "*.csproj").Length > 0)
                return true;

            if (Directory.GetFiles(dir, "*.sln").Length > 0)
                return true;

            if (Directory.Exists(Path.Combine(dir, ".git")))
                return true;

            if (File.Exists(Path.Combine(dir, "plan.txt")))
                return true;

            if (File.Exists(Path.Combine(dir, "version.txt")))
                return true;
        }
        catch
        {
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════
    //  AI #2 HELPERS
    //  Важно: AI #2 теперь строго отдельный.
    //  Token2 + ChatId2 обязательны.
    // ══════════════════════════════════════════════════════════
    static string GetAi2Token()
    {
        return Token2;
    }

    static string GetAi2Api()
    {
        return (!string.IsNullOrEmpty(ApiBaseUrl2) && ApiBaseUrl2 != DefaultApiBase)
            ? ApiBaseUrl2
            : ApiBaseUrl;
    }

    static string GetAi2Model()
    {
        return !string.IsNullOrEmpty(Ai2Model)
            ? Ai2Model
            : DefaultAi2Model;
    }

    static bool IsAi2Configured()
    {
        return !string.IsNullOrEmpty(Token2) && !string.IsNullOrEmpty(ChatId2);
    }

    // ══════════════════════════════════════════════════════════
    //  CHAT LINK / CHAT ID
    // ══════════════════════════════════════════════════════════
    static string ExtractChatId(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        input = input.Trim();

        if (Regex.IsMatch(input, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))
            return input;

        Match m = Regex.Match(input, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return m.Success ? m.Groups[1].Value : input;
    }

    static void ParseChatLink(string link, ref string baseUrl, ref string chatId)
    {
        if (string.IsNullOrWhiteSpace(link))
            return;

        link = link.Trim();

        string extractedId = ExtractChatId(link);
        if (!string.IsNullOrEmpty(extractedId) && extractedId != link)
            chatId = extractedId;

        int cIdx = link.IndexOf("/c/", StringComparison.OrdinalIgnoreCase);
        if (cIdx > 0)
        {
            baseUrl = link.Substring(0, cIdx).TrimEnd('/');
        }
        else
        {
            if (link.StartsWith("http://") || link.StartsWith("https://"))
                baseUrl = link.TrimEnd('/');
        }
    }

    // ══════════════════════════════════════════════════════════
    //  SAFE OUTPUT PATH
    //  Запрещает запись вне базовой папки.
    //  Абсолютный путь внутри базовой папки разрешён и приводится
    //  к относительному виду.
    // ══════════════════════════════════════════════════════════
    static bool TryResolveSafeOutputPath(string baseDir, string relativePath, out string safePath)
    {
        safePath = null;

        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        if (string.IsNullOrEmpty(baseDir))
            baseDir = BaseDir;

        try
        {
            string fullBase = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string baseNoSlash = fullBase.TrimEnd(Path.DirectorySeparatorChar);

            string rel = relativePath.Replace('\\', '/').Trim();

            while (rel.StartsWith("/"))
                rel = rel.Substring(1);

            if (string.IsNullOrWhiteSpace(rel))
                return false;

            if (Path.IsPathRooted(rel))
            {
                string rootedFull = Path.GetFullPath(rel);

                if (rootedFull.Equals(baseNoSlash, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!rootedFull.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                    return false;

                rel = rootedFull.Substring(fullBase.Length);

                if (string.IsNullOrWhiteSpace(rel))
                    return false;
            }

            foreach (string seg in rel.Split('/'))
            {
                if (seg == "..")
                    return false;
            }

            string combined = Path.Combine(fullBase, rel.Replace('/', Path.DirectorySeparatorChar));
            string fullPath = Path.GetFullPath(combined);

            if (!fullPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                return false;

            safePath = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }
}