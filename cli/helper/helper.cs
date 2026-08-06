// helper.cs — чтение истории чата Qwen (v7.1)
// Режимы: снапшот (по умолчанию), LIVE (--watch)
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
class MessageRec
{
public string Key;
public string Role;
public string Text;
}
class AuthExpiredException : Exception
{
public AuthExpiredException(string message) : base(message) { }
}
class QwenMessageFetcher
{
const string DefaultApiBase = "https://chat.qwen.ai";
const string PipeName = "NewEraMainPipe";
static readonly string BaseDir    = AppDomain.CurrentDomain.BaseDirectory;
static readonly string ConfigFile = Path.Combine(BaseDir, "qwen_config.txt");
static readonly string DumpFile   = Path.Combine(BaseDir, "last_chat.json");
static readonly string CursorFile = Path.Combine(BaseDir, "qwen_cursor.txt");

const int DefaultPollIntervalSec = 5;
const int PollBackoffMaxSec = 60;
const int PipeConnectTimeoutMs = 5000;
const int StatusEveryTicks = 6;

static string Token = null, ChatId = null, ApiBaseUrl = DefaultApiBase, CookieHeader = null;
static string Token2 = null, ApiBaseUrl2 = DefaultApiBase, ChatId2 = null, Ai2Link = null;
static bool NoPause = false, WatchMode = false, TailMode = false, PersistCursor = false;
static int PollInterval = DefaultPollIntervalSec;
static volatile bool StopRequested = false;

static int Main(string[] args)
{
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    // FIX: Убрано принудительное Console.InputEncoding = Encoding.UTF8
    
    Console.Title = "New Era v7 \u00B7 Helper";

    var positional = new List<string>();
    for (int i = 0; i < args.Length; i++) {
        string a = args[i];
        if (a == "--no-pause" || a == "-np") NoPause = true;
        else if (a == "--watch" || a == "-w") WatchMode = true;
        else if (a == "--tail" || a == "-t") { TailMode = true; WatchMode = true; }
        else if (a == "--persist-cursor") PersistCursor = true;
        else if (a == "--interval" && i + 1 < args.Length) {
            int v; if (int.TryParse(args[i + 1], out v) && v >= 1) PollInterval = v; i++;
        }
        else positional.Add(a);
    }

    Console.Title = WatchMode ? "New Era v7 \u00B7 Helper (live)" : "New Era v7 \u00B7 Helper";

    if (positional.Count >= 1) ParseChatLink(positional[0], ref ApiBaseUrl, ref ChatId);
    if (positional.Count >= 2) Token = positional[1];
    if (positional.Count >= 3) { Ai2Link = positional[2]; ParseChatLink(positional[2], ref ApiBaseUrl2, ref ChatId2); }
    if (positional.Count >= 4) Token2 = positional[3];

    if (string.IsNullOrEmpty(Token)) LoadConfig();

    if (ApiBaseUrl == DefaultApiBase && string.IsNullOrEmpty(ChatId)) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  AI #1 ссылка: ");
        Console.ResetColor();
        string input = Console.ReadLine();
        if (input != null && input.Trim().Length > 0) {
            string url = input.Trim();
            if (url.StartsWith("http://") || url.StartsWith("https://"))
                ParseChatLink(url, ref ApiBaseUrl, ref ChatId);
        }
    }

    if (string.IsNullOrEmpty(Token)) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  AI #1 токен: ");
        Console.ResetColor();
        string input = Console.ReadLine();
        Token = (input != null) ? input.Trim() : null;
    }

    if (ApiBaseUrl2 == DefaultApiBase || string.IsNullOrEmpty(ChatId2)) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  AI #2 ссылка: ");
        Console.ResetColor();
        string input = Console.ReadLine();
        if (input != null && input.Trim().Length > 0) {
            string url = input.Trim();
            if (url.StartsWith("http://") || url.StartsWith("https://")) {
                Ai2Link = url;
                string tmpBase = ApiBaseUrl2, tmpId = ChatId2;
                ParseChatLink(url, ref tmpBase, ref tmpId);
                ApiBaseUrl2 = tmpBase; ChatId2 = tmpId;
            }
        }
    }

    if (string.IsNullOrEmpty(Token2)) {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  AI #2 токен: ");
        Console.ResetColor();
        string input = Console.ReadLine();
        Token2 = (input != null) ? input.Trim() : null;
    }

    if (string.IsNullOrEmpty(ChatId) || string.IsNullOrEmpty(Token)) {
        WriteColored(ConsoleColor.Red, "  \u2716 Нет id или токена.\n");
        Pause();
        return 2;
    }

    SaveConfig();

    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }

    Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e) {
        StopRequested = true;
        e.Cancel = WatchMode;
    };

    int code = 0;
    try { code = WatchMode ? RunWatch() : RunOnce(); }
    catch (Exception ex) {
        WriteColored(ConsoleColor.Red, "  \u2716 Критическая ошибка: " + ex.Message + "\n");
        code = 1;
    }
    Pause();
    return code;
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

static int RunOnce()
{
    try {
        WriteColored(ConsoleColor.DarkGray, "  \u25CC Загрузка истории...\n");
        string json = FetchRawJson();
        List<string> messages = ParseAssistantMessages(json);
        if (messages.Count == 0) { WriteColored(ConsoleColor.Yellow, "  \u26A0 Ответов не найдено.\n"); return 0; }
        bool sent = SendBatch(messages, "snapshot");
        if (sent) WriteColored(ConsoleColor.Green, "  \u2714 Отправлено в main: " + messages.Count + " сообщ.\n");
        else {
            Console.WriteLine();
            foreach (string msg in messages) { Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("  " + msg.Replace("\n", "\n")); Console.ResetColor(); Console.WriteLine(); }
        }
        return 0;
    }
    catch (AuthExpiredException aex) { WriteColored(ConsoleColor.Red, "  \u2716 " + aex.Message + "\n"); return 1; }
    catch (WebException ex) { WriteColored(ConsoleColor.Red, "  \u2716 Сеть: " + ex.Message + "\n"); return 1; }
    catch (Exception ex) { WriteColored(ConsoleColor.Red, "  \u2716 Ошибка: " + ex.Message + "\n"); return 1; }
}

static int RunWatch()
{
    var seen = new HashSet<string>();
    if (PersistCursor) LoadCursor(seen);
    bool first = true;
    int backoff = PollInterval, tick = 0;
    WriteColored(ConsoleColor.Green, "\n\u25CF LIVE MODE \u00B7 ctrl+c — стоп\n");
    while (!StopRequested) {
        tick++;
        int sleepSec = PollInterval;
        try {
            string json = null;
            bool authStop = false;
            try { json = FetchRawJson(); }
            catch (AuthExpiredException aex) { WriteColored(ConsoleColor.Red, "  \u2716 " + aex.Message + "\n"); authStop = true; }
            catch (WebException) { sleepSec = backoff; backoff = Math.Min(backoff * 2, PollBackoffMaxSec); }
            catch (Exception) { sleepSec = backoff; backoff = Math.Min(backoff * 2, PollBackoffMaxSec); }
            if (authStop) break;

            if (json != null) {
                backoff = PollInterval;
                List<MessageRec> msgs = BuildOrderedMessages(json);
                if (msgs != null) {
                    var delta = new List<MessageRec>();
                    foreach (var m in msgs) if (!seen.Contains(m.Key)) delta.Add(m);
                    foreach (var m in msgs) seen.Add(m.Key);
                    var newAnswers = new List<string>();
                    foreach (var m in delta)
                        if ((m.Role == "assistant" || m.Role == "model") && !string.IsNullOrWhiteSpace(m.Text))
                            newAnswers.Add(m.Text);
                    if (first) {
                        first = false;
                        if (TailMode) WriteColored(ConsoleColor.DarkGray, "  \u25CC Ожидание новых... (запомнено " + seen.Count + ")\n");
                        else WriteColored(ConsoleColor.DarkGray, "  \u25CC История: " + newAnswers.Count + " ответов.\n");
                    }
                    bool show = newAnswers.Count > 0 && !(TailMode && tick == 1);
                    if (show) {
                        string mode = (tick == 1 && !TailMode) ? "snapshot" : "watch";
                        bool sent = SendBatch(newAnswers, mode);
                        if (!sent) {
                            Console.WriteLine();
                            foreach (string a in newAnswers) { Console.ForegroundColor = ConsoleColor.White; Console.WriteLine("  " + a.Replace("\n", "\n")); Console.ResetColor(); Console.WriteLine(); }
                        } else WriteColored(ConsoleColor.Green, "  \u2714 +" + newAnswers.Count + " сообщ. \u2192 main\n");
                    } else if (tick % StatusEveryTicks == 0)
                        WriteColored(ConsoleColor.DarkGray, "  \u00B7 tick " + tick + " \u00B7 новых: 0\n");
                    if (PersistCursor && tick % StatusEveryTicks == 0) SaveCursor(seen);
                }
            }
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Red, "  \u2716 " + ex.Message + "\n");
            sleepSec = backoff;
            backoff = Math.Min(backoff * 2, PollBackoffMaxSec);
        }
        int totalMs = sleepSec * 1000, slept = 0;
        while (slept < totalMs && !StopRequested) { Thread.Sleep(200); slept += 200; }
    }
    if (PersistCursor) SaveCursor(seen);
    WriteColored(ConsoleColor.DarkGray, "\n\u25C2 Остановлен.\n");
    return 0;
}

static string FetchRawJson()
{
    string url = ApiBaseUrl.TrimEnd('/') + "/api/v2/chats/" + Uri.EscapeDataString(ChatId);
    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
    req.Method = "GET";
    req.Timeout = 30000;
    req.ReadWriteTimeout = 60000;
    req.KeepAlive = false;
    ApplyAuth(req);
    req.Accept = "application/json, text/plain, */*";
    req.Headers["source"] = "web";
    req.Headers["Origin"] = "https://chat.qwen.ai";
    req.Referer = "https://chat.qwen.ai/";
    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
    req.Headers["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.7";
    req.Headers["Cache-Control"] = "no-cache";

    string jsonResponse;
    try {
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (Stream stream = resp.GetResponseStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
            jsonResponse = reader.ReadToEnd();
        }
    }
    catch (WebException wex) {
        HttpWebResponse r = wex.Response as HttpWebResponse;
        if (r != null && ((int)r.StatusCode == 401 || (int)r.StatusCode == 403))
            throw new AuthExpiredException("токен истёк (HTTP " + (int)r.StatusCode + ")");
        throw;
    }

    string trimmed = jsonResponse.TrimStart();
    if (trimmed.StartsWith("<"))
        throw new AuthExpiredException("сервер вернул HTML — токен истёк или WAF");

    for (int dumpAttempt = 0; dumpAttempt < 3; dumpAttempt++) {
        try { File.WriteAllText(DumpFile, jsonResponse, new UTF8Encoding(false)); break; }
        catch (IOException) { if (dumpAttempt < 2) Thread.Sleep(100); }
        catch { break; }
    }
    return jsonResponse;
}

static string PipeEncode(string text)
{
    if (text == null) return "";
    var sb = new StringBuilder();
    foreach (char c in text) {
        if (c == '\\') sb.Append("\\\\");
        else if (c == '\n') sb.Append("\\n");
        else if (c == '\r') { }
        else sb.Append(c);
    }
    return sb.ToString();
}

static bool SendBatch(List<string> messages, string mode)
{
    if (messages == null || messages.Count == 0) return false;
    for (int attempt = 0; attempt < 3; attempt++) {
        try {
            using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out)) {
                client.Connect(PipeConnectTimeoutMs);
                using (var writer = new StreamWriter(client, Encoding.UTF8)) {
                    writer.AutoFlush = true;
                    writer.WriteLine("[BATCH count=" + messages.Count + " mode=" + mode + "]");
                    for (int i = 0; i < messages.Count; i++)
                        writer.WriteLine("[#" + (i + 1) + "] " + PipeEncode(messages[i].Replace("\r\n", "\n")));
                    writer.WriteLine("[END]");
                }
            }
            return true;
        } catch {
            if (attempt < 2) Thread.Sleep(300 * (attempt + 1));
        }
    }
    return false;
}

static void ApplyAuth(HttpWebRequest req)
{
    if (string.IsNullOrEmpty(Token)) return;
    req.Headers[HttpRequestHeader.Authorization] = "Bearer " + Token;
    try {
        var cc = new CookieContainer();
        string cookie = !string.IsNullOrEmpty(CookieHeader) ? CookieHeader : ("token=" + Token);
        cc.SetCookies(new Uri(ApiBaseUrl), cookie);
        req.CookieContainer = cc;
    } catch {
        try { req.Headers[HttpRequestHeader.Cookie] = !string.IsNullOrEmpty(CookieHeader) ? CookieHeader : ("token=" + Token); } catch { }
    }
}

static List<MessageRec> BuildOrderedMessages(string json)
{
    JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    object rootObj;
    try { rootObj = serializer.DeserializeObject(json); } catch { return null; }
    var byId = new Dictionary<string, Dictionary<string, object>>();
    var order = new List<string>();
    CollectMessages(rootObj, byId, order);
    if (order.Count == 0) return null;
    var recs = new List<MessageRec>();
    foreach (string id in order) {
        Dictionary<string, object> msg;
        if (!byId.TryGetValue(id, out msg)) continue;
        string role = msg.ContainsKey("role") ? msg["role"] as string : null;
        string raw = ExtractText(msg);
        string key = msg.ContainsKey("id") ? msg["id"] as string : null;
        if (string.IsNullOrEmpty(key)) key = "h:" + Fnv1aHex((role ?? "") + "|" + (raw ?? ""));
        recs.Add(new MessageRec { Key = key, Role = role, Text = string.IsNullOrWhiteSpace(raw) ? null : CleanContent(raw) });
    }
    return recs;
}

static List<string> ParseAssistantMessages(string json)
{
    List<MessageRec> recs = BuildOrderedMessages(json);
    if (recs == null) return new List<string>();
    var messages = new List<string>();
    var seenText = new HashSet<string>();
    foreach (var r in recs) {
        if (r.Role != "assistant" && r.Role != "model") continue;
        if (string.IsNullOrWhiteSpace(r.Text)) continue;
        if (seenText.Add(r.Text)) messages.Add(r.Text);
    }
    return messages;
}

static void CollectMessages(object node, Dictionary<string, Dictionary<string, object>> byId, List<string> order)
{
    if (node == null) return;
    Dictionary<string, object> dict = node as Dictionary<string, object>;
    if (dict != null) {
        string role = dict.ContainsKey("role") ? dict["role"] as string : null;
        if (role == "user" || role == "assistant" || role == "model" || role == "system") {
            string id = dict.ContainsKey("id") ? dict["id"] as string : null;
            if (string.IsNullOrEmpty(id)) id = "__auto_" + order.Count;
            if (!byId.ContainsKey(id)) { byId[id] = dict; order.Add(id); }
            foreach (var kv in dict) {
                if (kv.Key == "role" || kv.Key == "id") continue;
                CollectMessages(kv.Value, byId, order);
            }
            return;
        }
        foreach (var kv in dict) CollectMessages(kv.Value, byId, order);
        return;
    }
    object[] arr = node as object[];
    if (arr != null) for (int i = 0; i < arr.Length; i++) CollectMessages(arr[i], byId, order);
}

static string ExtractText(object node)
{
    if (node == null) return null;
    string s = node as string;
    if (s != null) return s;
    Dictionary<string, object> dict = node as Dictionary<string, object>;
    if (dict != null) {
        if (dict.ContainsKey("phase")) {
            string ph = dict["phase"] as string;
            if (ph != null) {
                string pl = ph.ToLowerInvariant();
                if (pl.Contains("think") || pl.Contains("reason") || pl.Contains("summary") || pl.Contains("reflection")) return null;
            }
        }
        foreach (string key in new[] { "content_list", "content", "text", "message" }) {
            if (dict.ContainsKey(key)) {
                string t = ExtractText(dict[key]);
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
        }
        return null;
    }
    object[] arr = node as object[];
    if (arr != null) {
        var sb = new StringBuilder();
        foreach (object el in arr) {
            string t = ExtractText(el);
            if (!string.IsNullOrWhiteSpace(t)) { if (sb.Length > 0) sb.Append("\n"); sb.Append(t); }
        }
        return sb.ToString();
    }
    return null;
}

static string CleanContent(string c)
{
    if (string.IsNullOrEmpty(c)) return "";
    c = Regex.Replace(c, "<[^>]*>", " ");
    c = Regex.Replace(c, @"[ \t]+", " ");
    c = Regex.Replace(c, @"(?m)^[ ]+|[ ]+$", "");
    c = Regex.Replace(c, @"(\r?\n){3,}", "\n\n");
    return c.Trim();
}

static string Fnv1aHex(string s)
{
    if (s == null) s = "";
    const ulong prime = 1099511628211UL;
    ulong hash = 14695981039346656037UL;
    for (int i = 0; i < s.Length; i++) { hash ^= (ulong)s[i]; hash *= prime; }
    return hash.ToString("x16");
}

static string ReadTextAuto(string path)
{
    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
    byte[] raw;
    try { raw = File.ReadAllBytes(path); } catch { return ""; }
    if (raw == null || raw.Length == 0) return "";
    Encoding enc; int skip = 0;
    if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) { enc = Encoding.UTF8; skip = 3; }
    else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE) { enc = Encoding.Unicode; skip = 2; }
    else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF) { enc = Encoding.BigEndianUnicode; skip = 2; }
    else if (raw.Length >= 2 && raw[0] != 0 && raw[1] == 0) { enc = Encoding.Unicode; }
    else if (raw.Length >= 2 && raw[0] == 0 && raw[1] != 0) { enc = Encoding.BigEndianUnicode; }
    else { enc = Encoding.UTF8; }
    string result;
    try { result = enc.GetString(raw, skip, raw.Length - skip); }
    catch { result = Encoding.UTF8.GetString(raw, skip, raw.Length - skip); }
    if (result.Length > 0 && result[0] == '\uFEFF') result = result.Substring(1);
    return result.Replace("\0", "");
}

static void LoadConfig()
{
    if (!File.Exists(ConfigFile)) return;
    for (int attempt = 0; attempt < 3; attempt++) {
        try {
            foreach (string line in ReadTextAuto(ConfigFile).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) {
                string t = line.Trim();
                if (t.StartsWith("#")) continue;
                if (t.StartsWith("CHAT_ID=") && string.IsNullOrEmpty(ChatId)) ChatId = ExtractChatId(t.Substring(8).Trim());
                else if (t.StartsWith("TOKEN=") && string.IsNullOrEmpty(Token)) Token = t.Substring(6).Trim();
                else if (t.StartsWith("API_URL=") && ApiBaseUrl == DefaultApiBase) {
                    string url = t.Substring(8).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) ApiBaseUrl = url;
                }
                else if (t.StartsWith("COOKIE=") && string.IsNullOrEmpty(CookieHeader)) CookieHeader = t.Substring(7).Trim();
                else if (t.StartsWith("AI2_TOKEN=") && string.IsNullOrEmpty(Token2)) Token2 = t.Substring(10).Trim();
                else if (t.StartsWith("AI2_API_URL=") && ApiBaseUrl2 == DefaultApiBase) {
                    string url = t.Substring(12).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) ApiBaseUrl2 = url;
                }
                else if (t.StartsWith("AI2_LINK=")) {
                    string url = t.Substring(9).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://")) {
                        Ai2Link = url;
                        string tmpBase = ApiBaseUrl2, tmpId = ChatId2;
                        ParseChatLink(url, ref tmpBase, ref tmpId);
                        ApiBaseUrl2 = tmpBase;
                        if (!string.IsNullOrEmpty(tmpId)) ChatId2 = tmpId;
                    }
                }
                else if (t.StartsWith("AI2_CHAT_ID=") && string.IsNullOrEmpty(ChatId2)) ChatId2 = ExtractChatId(t.Substring(12).Trim());
            }
            return;
        }
        catch (IOException) { Thread.Sleep(200 * (attempt + 1)); }
        catch { return; }
    }
}

static void SaveConfig()
{
    try {
        var sb = new StringBuilder();
        sb.AppendLine("CHAT_ID=" + (ChatId ?? ""));
        sb.AppendLine("TOKEN=" + (Token ?? ""));
        sb.AppendLine("API_URL=" + (ApiBaseUrl ?? DefaultApiBase));
        if (!string.IsNullOrEmpty(CookieHeader)) sb.AppendLine("COOKIE=" + CookieHeader);
        if (!string.IsNullOrEmpty(Ai2Link)) sb.AppendLine("AI2_LINK=" + Ai2Link);
        else {
            if (!string.IsNullOrEmpty(ChatId2)) sb.AppendLine("AI2_CHAT_ID=" + ChatId2);
            if (ApiBaseUrl2 != DefaultApiBase) sb.AppendLine("AI2_API_URL=" + ApiBaseUrl2);
        }
        if (!string.IsNullOrEmpty(Token2)) sb.AppendLine("AI2_TOKEN=" + Token2);
        File.WriteAllText(ConfigFile, sb.ToString(), new UTF8Encoding(false));
    } catch { }
}

static string ExtractChatId(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return null;
    input = input.Trim();
    if (Regex.IsMatch(input, @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")) return input;
    Match m = Regex.Match(input, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
    return m.Success ? m.Groups[1].Value : input;
}

static void LoadCursor(HashSet<string> seen)
{
    if (!File.Exists(CursorFile)) return;
    try {
        foreach (string line in ReadTextAuto(CursorFile).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)) {
            string k = line.Trim();
            if (k.Length > 0) seen.Add(k);
        }
    } catch { }
}

static void SaveCursor(HashSet<string> seen)
{
    try {
        var sb = new StringBuilder();
        foreach (string k in seen) sb.AppendLine(k);
        File.WriteAllText(CursorFile, sb.ToString(), new UTF8Encoding(false));
    } catch { }
}

static void Pause()
{
    if (NoPause || WatchMode) return;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("\nenter — закрыть.");
    Console.ResetColor();
    try { Console.ReadLine(); } catch { }
}

static void WriteColored(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.Write(text ?? "");
    Console.ResetColor();
}
}