// Http.cs — единый HTTP-клиент: Primary + AI#2, SSE-парсер, WAF, retry
// New Era v7.2 · FIX CS0103: возвращён ShouldUseThinking + legacy-методы
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

partial class MainConsole
{
    // Внешний цикл: максимум 10 отправок промпта
    const int MaxAttempts   = 10;
    // Внутренний транспортный цикл (HTTP-ошибки, WAF)
    const int InnerAttempts = 3;
    const int RetryDelayMs   = 2000;
    const int WafBaseDelayMs = 30000;
    const int WafMaxDelayMs  = 120000;

    const int PrimaryTimeoutMs           = 120000;
    const int PrimaryReadWriteTimeoutMs  = 180000;
    const int DispatchTimeoutMs          = 90000;
    const int DispatchReadWriteTimeoutMs = 120000;

    const int RolePauseMs = 3000;
    const int PrimaryPauseAfterDispatchMs = 5000;

    // WAF cooldown: long + Interlocked (volatile DateTime нельзя — CS0677)
    static long LastWafBlockTicks = 0;
    const int WafCooldownSeconds = 30;

    static readonly object RndLock = new object();
    static readonly Random Rnd = new Random();

    static int GetJitter(int maxMs)
    {
        lock (RndLock) { return Rnd.Next(0, maxMs); }
    }

    // ══════════════════════════════════════════════
    //  WAF / SERVER BUSY DETECTION
    // ══════════════════════════════════════════════
    static bool LooksLikeWafBlock(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("rgv587_flag") || text.Contains("\"action\":\"deny\"") ||
               text.Contains("AliyunCaptcha") || text.Contains("aliyun_waf") ||
               text.Contains("punish:resource:template") ||
               text.Contains("qrcode=") || text.Contains("uuid=");
    }

    static bool LooksLikeServerBusy(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string t = text.ToLowerInvariant();
        return t.Contains("high load") ||
               t.Contains("overload") ||
               t.Contains("too many requests") ||
               t.Contains("rate limit") ||
               t.Contains("service unavailable") ||
               t.Contains("service is busy") ||
               t.Contains("server is busy") ||
               t.Contains("высокую нагрузку") ||
               t.Contains("высокая нагрузка") ||
               t.Contains("попробуйте позже") ||
               t.Contains("попробуйте ещё раз") ||
               t.Contains("quota exceeded") ||
               t.Contains("insufficient_quota") ||
               t.Contains("internal server error") ||
               t.Contains("bad gateway") ||
               t.Contains("http/1.1 503") ||
               t.Contains("http/1.1 429") ||
               t.Contains("http/1.1 529") ||
               t.Contains("\"status\":503") ||
               t.Contains("\"status\":429") ||
               t.Contains("\"code\":\"429\"") ||
               t.Contains("\"code\":\"503\"");
    }

    static void MarkWafBlock()
    {
        Interlocked.Exchange(ref LastWafBlockTicks, DateTime.UtcNow.Ticks);
    }

    static void EnforceWafCooldown()
    {
        try {
            long ticks = Interlocked.Read(ref LastWafBlockTicks);
            if (ticks == 0) return;
            DateTime last = new DateTime(ticks, DateTimeKind.Utc);
            TimeSpan since = DateTime.UtcNow - last;
            if (since.TotalSeconds < WafCooldownSeconds) {
                int waitMs = (int)((WafCooldownSeconds - since.TotalSeconds) * 1000);
                if (waitMs > 0) {
                    WriteColored(ConsoleColor.DarkGray,
                        "  \u25CC WAF cooldown: " + (waitMs / 1000) + "с...\n");
                    SleepInterruptible(waitMs);
                }
            }
        } catch { }
    }

    static void SleepWaf(int attempt, string who)
    {
        int baseDelay = Math.Min(WafBaseDelayMs * (attempt + 1), WafMaxDelayMs);
        int jitter = GetJitter(5000);
        int delay = baseDelay + jitter;
        WriteColored(ConsoleColor.Yellow,
            "    \u26A0 WAF (" + who + "): пауза " + (delay / 1000) + "с...\n");
        int slept = 0;
        while (slept < delay && !StopRequested) { Thread.Sleep(500); slept += 500; }
    }

    static void SleepInterruptible(int ms)
    {
        int slept = 0;
        while (slept < ms && !StopRequested) { Thread.Sleep(500); slept += 500; }
    }

    // ══════════════════════════════════════════════
    //  FIX CS0103: проверка thinking-режима модели
    // ══════════════════════════════════════════════
    static bool ShouldUseThinking(string model)
    {
        string m = (model ?? "").ToLowerInvariant();
        return m.Contains("3.8") || m.Contains("preview") || m == (PrimaryModel ?? "").ToLowerInvariant();
    }

    // ══════════════════════════════════════════════
    //  LEGACY-МЕТОДЫ (совместимость со старым Commands.cs)
    // ══════════════════════════════════════════════
    static string PostMessage(string text, string parentId)
    {
        return PostMessageInternal(text, parentId, PrimaryModel, ApiBaseUrl,
            PrimaryTimeoutMs, PrimaryReadWriteTimeoutMs, Token, ChatId, true);
    }

    static string PostRoleChatMessage(
        string roleName, string systemPrompt, string userPrompt,
        string model, string apiBase, string token, string chatId,
        int timeoutMs, int rwTimeoutMs)
    {
        if (string.IsNullOrEmpty(userPrompt)) throw new Exception(roleName + ": пустой запрос");
        if (string.IsNullOrEmpty(token))      throw new Exception(roleName + ": нет токена");
        if (string.IsNullOrEmpty(chatId))     throw new Exception(roleName + ": нет chat_id");

        string fullPrompt = string.IsNullOrEmpty(systemPrompt)
            ? userPrompt : systemPrompt + "\n" + userPrompt;

        bool isPrimaryChat = (token == Token && chatId == ChatId);
        bool thinking = isPrimaryChat || ShouldUseThinking(model);

        return PostMessageInternal(fullPrompt, null, model, apiBase,
            timeoutMs, rwTimeoutMs, token, chatId, thinking);
    }

    // ══════════════════════════════════════════════
    //  RETRY-ОБЁРТКА (до 10 попыток)
    // ══════════════════════════════════════════════
    static string PostAndParseWithRetry(
        string text, string parentId,
        string model, string apiBase,
        int timeoutMs, int rwTimeoutMs,
        string token, string chatId, bool thinking,
        Action<string> parentSetter, string who, string dumpFile)
    {
        Exception lastEx = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (StopRequested) return null;

            if (attempt > 1)
            {
                int delay;
                if (lastEx != null && lastEx.Message.IndexOf("WAF", StringComparison.Ordinal) >= 0)
                    delay = WafBaseDelayMs + GetJitter(5000);
                else
                    delay = Math.Min(RetryDelayMs * attempt, 60000) + GetJitter(2000);

                WriteColored(ConsoleColor.Yellow,
                    "  \u21BB " + who + ": сервер не обработал запрос (" +
                    (lastEx != null ? lastEx.Message : "пустой ответ") +
                    ") — попытка " + attempt + "/" + MaxAttempts +
                    " через " + (delay / 1000) + "с...\n");
                SleepInterruptible(delay);
            }

            string raw;
            try
            {
                raw = PostMessageInternal(text, parentId, model, apiBase,
                    timeoutMs, rwTimeoutMs, token, chatId, thinking);
            }
            catch (Exception ex) { lastEx = ex; continue; }

            if (!string.IsNullOrEmpty(dumpFile))
            { try { File.WriteAllText(dumpFile, raw ?? "", new UTF8Encoding(false)); } catch { } }

            string trimmedRaw = (raw ?? "").TrimStart();
            bool isSse = trimmedRaw.StartsWith("data:");

            if (!isSse && (LooksLikeWafBlock(trimmedRaw) || LooksLikeServerBusy(trimmedRaw)))
            {
                if (LooksLikeWafBlock(trimmedRaw)) MarkWafBlock();
                lastEx = new Exception(LooksLikeWafBlock(trimmedRaw) ? "WAF/капча" : "сервер перегружен");
                continue;
            }

            string parsed = ParseSseAnswerEx(raw, parentSetter);
            if (!string.IsNullOrWhiteSpace(parsed)) return parsed;

            lastEx = new Exception("пустой ответ");
        }

        throw new Exception(who + ": " + (lastEx != null ? lastEx.Message : "нет ответа") +
            " после " + MaxAttempts + " попыток");
    }

    static string PostMessageWithRetry(string text, string parentId)
    {
        return PostAndParseWithRetry(text, parentId, PrimaryModel, ApiBaseUrl,
            PrimaryTimeoutMs, PrimaryReadWriteTimeoutMs, Token, ChatId, true,
            new Action<string>(SetPrimaryParent), "Primary", DumpFile);
    }

    static string PostRoleMessageWithRetry(
        string roleName, string systemPrompt, string userPrompt,
        string model, string apiBase, string token, string chatId,
        int timeoutMs, int rwTimeoutMs)
    {
        if (string.IsNullOrEmpty(userPrompt)) throw new Exception(roleName + ": пустой запрос");
        if (string.IsNullOrEmpty(token))      throw new Exception(roleName + ": нет токена");
        if (string.IsNullOrEmpty(chatId))     throw new Exception(roleName + ": нет chat_id");

        string fullPrompt = string.IsNullOrEmpty(systemPrompt)
            ? userPrompt : systemPrompt + "\n" + userPrompt;

        bool isPrimaryChat = (token == Token && chatId == ChatId);
        bool thinking = isPrimaryChat || ShouldUseThinking(model);

        Action<string> setter = isPrimaryChat
            ? new Action<string>(SetPrimaryParent)
            : new Action<string>(SetAi2Parent);

        return PostAndParseWithRetry(fullPrompt, null, model, apiBase,
            timeoutMs, rwTimeoutMs, token, chatId, thinking,
            setter, roleName, isPrimaryChat ? DumpFile : null);
    }

    static string PostDispatchMessageWithRetry(string systemPrompt, string userPrompt)
    {
        if (!IsAi2Configured())
            throw new Exception("AI #2 не сконфигурирован");

        string fullPrompt = string.IsNullOrEmpty(systemPrompt)
            ? userPrompt : systemPrompt + "\n" + userPrompt;

        return PostAndParseWithRetry(fullPrompt, null, GetAi2Model(), GetAi2Api(),
            DispatchTimeoutMs, DispatchReadWriteTimeoutMs, GetAi2Token(), ChatId2, false,
            new Action<string>(SetAi2Parent), "AI #2", null);
    }

    // ══════════════════════════════════════════════
    //  ЕДИНЫЙ POST (Primary + AI#2)
    // ══════════════════════════════════════════════
    static string PostMessageInternal(
        string text, string parentId, string model, string apiBase,
        int timeoutMs, int rwTimeoutMs, string token, string chatId, bool thinking)
    {
        EnforceWafCooldown();

        string effModel = string.IsNullOrEmpty(model) ? PrimaryModel : model;
        string effBase  = string.IsNullOrEmpty(apiBase) ? ApiBaseUrl : apiBase;
        string effChat  = chatId;
        bool isPrimary  = (token == Token && chatId == ChatId);
        string parent   = isPrimary ? LastResponseId : LastAi2ResponseId;

        string url = effBase.TrimEnd('/') + "/api/v2/chat/completions";
        if (!string.IsNullOrEmpty(effChat))
            url += "?chat_id=" + Uri.EscapeDataString(effChat);

        int ts = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        string msgId = Guid.NewGuid().ToString();

        string featureConfig = thinking
            ? "\"feature_config\":{\"thinking_enabled\":true,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"Thinking\",\"thinking_format\":\"summary\",\"auto_search\":true}"
            : "\"feature_config\":{\"thinking_enabled\":false,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"None\",\"thinking_format\":\"summary\",\"auto_search\":false}";

        string payload =
            "{\"stream\":true,\"version\":\"3\",\"incremental_output\":true,\"chat_id\":" + JsonStr(effChat) +
            ",\"chat_mode\":\"normal\",\"model\":" + JsonStr(effModel) +
            ",\"parent_id\":" + JsonStr(parent) +
            ",\"messages\":[{\"fid\":\"" + msgId + "\",\"parentId\":" + JsonStr(parent) +
            ",\"childrenIds\":[],\"role\":\"user\",\"content\":" + EscapeJson(text) +
            ",\"user_action\":\"chat\",\"files\":[],\"timestamp\":" + ts +
            ",\"models\":[" + JsonStr(effModel) +
            "],\"chat_type\":\"t2t\"," + featureConfig +
            ",\"extra\":{\"meta\":{\"subChatType\":\"t2t\"}},\"sub_chat_type\":\"t2t\"}],\"timestamp\":" + (ts + 1) + "}";

        byte[] bodyBytes = Encoding.UTF8.GetBytes(payload);
        Exception lastEx = null;

        for (int attempt = 0; attempt < InnerAttempts; attempt++)
        {
            if (StopRequested) break;

            if (attempt > 0)
            {
                WriteColored(ConsoleColor.DarkGray,
                    "    \u21BB Повтор " + (attempt + 1) + "/" + InnerAttempts + "...\n");
                int backoff = RetryDelayMs * attempt + GetJitter(1000);
                SleepInterruptible(backoff);
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = rwTimeoutMs;
            req.ContentType = "application/json";
            req.Accept = "application/json, text/plain, */*";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = false;

            req.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            string cookieVal = (!string.IsNullOrEmpty(CookieHeader) && token == Token)
                ? CookieHeader : ("token=" + token);

            try {
                var cc = new CookieContainer();
                cc.SetCookies(new Uri(effBase), cookieVal);
                req.CookieContainer = cc;
            } catch {
                try { req.Headers[HttpRequestHeader.Cookie] = cookieVal; } catch { }
            }

            req.Headers["source"] = "web";
            req.Headers["Origin"] = "https://chat.qwen.ai";
            req.Referer = "https://chat.qwen.ai/c/" + (effChat ?? "");
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
            req.Headers["version"] = QwenVersion;
            req.Headers["x-accel-buffering"] = "no";
            req.Headers["x-request-id"] = Guid.NewGuid().ToString();
            req.Headers["timezone"] = "Europe/Moscow";
            req.Headers["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.7";
            req.Headers["Cache-Control"] = "no-cache";

            try { req.Headers["sec-ch-ua"] = "\"Chromium\";v=\"126\", \"Google Chrome\";v=\"126\", \"Not?A_Brand\";v=\"99\""; } catch { }
            try { req.Headers["sec-ch-ua-mobile"] = "?0"; } catch { }
            try { req.Headers["sec-ch-ua-platform"] = "\"Windows\""; } catch { }
            try { req.Headers["sec-fetch-dest"] = "empty"; } catch { }
            try { req.Headers["sec-fetch-mode"] = "cors"; } catch { }
            try { req.Headers["sec-fetch-site"] = "same-origin"; } catch { }

            req.ContentLength = bodyBytes.Length;

            // P0: гарантированное освобождение request stream
            try {
                using (Stream rs = req.GetRequestStream()) {
                    rs.Write(bodyBytes, 0, bodyBytes.Length);
                }
            } catch (Exception ex) {
                lastEx = ex;
                continue;
            }

            string response;
            try {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
                    response = reader.ReadToEnd();
                }
            }
            catch (WebException wex) {
                HttpWebResponse r = wex.Response as HttpWebResponse;
                int code = -1; string errBody = "";
                try {
                    if (r != null) {
                        code = (int)r.StatusCode;
                        using (Stream es = r.GetResponseStream())
                        using (StreamReader er = new StreamReader(es, Encoding.UTF8)) {
                            errBody = er.ReadToEnd();
                        }
                    }
                } catch { } finally {
                    if (r != null) { try { r.Close(); } catch { } }
                }

                string trimmedErr = (errBody ?? "").TrimStart();

                if (trimmedErr.StartsWith("<") || LooksLikeWafBlock(trimmedErr)) {
                    MarkWafBlock();
                    lastEx = new Exception("HTTP " + code + " — WAF/капча");
                    if (attempt + 1 < InnerAttempts) { SleepWaf(attempt, "HTTP"); continue; }
                    throw new Exception("WAF блокирует после " + InnerAttempts + " попыток.");
                }

                if (code == 401 || code == 403)
                    throw new Exception("HTTP " + code + " — токен недействителен.");

                if (code == 429 || code >= 500 || code == -1 || LooksLikeServerBusy(trimmedErr)) {
                    lastEx = new Exception("HTTP " + code + " (сервер занят)");
                    if (attempt + 1 < InnerAttempts) continue;
                }

                string head = (errBody ?? "").Replace("\r", " ").Replace("\n", " ");
                if (head.Length > 300) head = head.Substring(0, 300) + "...";
                throw new Exception("HTTP " + code + " " + head);
            }

            string trimmed = (response ?? "").TrimStart();

            if (trimmed.Length == 0) {
                lastEx = new Exception("пустое тело");
                if (attempt + 1 < InnerAttempts) continue;
                throw new Exception("пустое тело после " + InnerAttempts + " попыток");
            }

            if (trimmed.StartsWith("<")) {
                if (trimmed.Contains("aliyun_waf") || trimmed.Contains("AliyunCaptcha")) {
                    MarkWafBlock();
                    lastEx = new Exception("WAF (HTML)");
                    if (attempt + 1 < InnerAttempts) { SleepWaf(attempt, "HTTP"); continue; }
                    throw new Exception("WAF блокирует после " + InnerAttempts + " попыток.");
                }
                throw new Exception("сервер вернул HTML.");
            }

            if (LooksLikeWafBlock(trimmed)) {
                MarkWafBlock();
                lastEx = new Exception("WAF/captcha block");
                if (attempt + 1 < InnerAttempts) { SleepWaf(attempt, "HTTP"); continue; }
                throw new Exception("WAF блокирует после " + InnerAttempts + " попыток.");
            }

            if (trimmed.StartsWith("{") && trimmed.Contains("\"error\"")) {
                string snippet = trimmed.Length > 400 ? trimmed.Substring(0, 400) + "..." : trimmed;
                lastEx = new Exception("API: " + snippet);
                if (attempt + 1 < InnerAttempts) continue;
                throw lastEx;
            }

            return response;
        }

        throw lastEx ?? new Exception("не удалось отправить запрос");
    }

    // ══════════════════════════════════════════════
    //  SSE PARSER
    // ══════════════════════════════════════════════
    static void SetPrimaryParent(string id) { if (!string.IsNullOrEmpty(id)) LastResponseId = id; }
    static void SetAi2Parent(string id)   { if (!string.IsNullOrEmpty(id)) LastAi2ResponseId = id; }

    static string ParseSseAnswer(string raw)
    {
        return ParseSseAnswerEx(raw, new Action<string>(SetPrimaryParent));
    }

    static string ParseSseAnswerEx(string raw, Action<string> parentSetter)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        var answer = new StringBuilder();
        string newResponseId = null;
        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        foreach (string rawLine in raw.Split(new[] { "\n" }, StringSplitOptions.None)) {
            string t = rawLine.Trim();
            if (!t.StartsWith("data:")) continue;

            string data = t.Substring(5).Trim();
            if (data.Length == 0 || data == "[DONE]") continue;

            try {
                var obj = ser.DeserializeObject(data) as Dictionary<string, object>;
                if (obj == null) continue;

                string rid = FindJsonString(obj, "response_id", 0);
                if (!string.IsNullOrEmpty(rid)) newResponseId = rid;

                if (obj.ContainsKey("status")) {
                    string st = obj["status"] as string;
                    if (st == "finished" || st == "completed") break;
                }

                string phase = FindJsonString(obj, "phase", 0);
                if (IsThinkingPhase(phase)) continue;

                string piece = ExtractDeltaContent(obj);
                if (!string.IsNullOrEmpty(piece))
                    AppendStreamPiece(answer, piece);
            } catch { }
        }

        if (!string.IsNullOrEmpty(newResponseId) && parentSetter != null)
            parentSetter(newResponseId);

        string result = answer.ToString().Trim();
        if (result.Length > 0) return result;

        return ParseFallback(raw);
    }

    static string ExtractDeltaContent(Dictionary<string, object> obj)
    {
        if (!obj.ContainsKey("choices")) return null;
        object[] choices = obj["choices"] as object[];
        if (choices == null || choices.Length == 0) return null;

        var sb = new StringBuilder();
        foreach (object ch in choices) {
            var choice = ch as Dictionary<string, object>;
            if (choice == null) continue;
            if (!choice.ContainsKey("delta")) continue;

            var delta = choice["delta"] as Dictionary<string, object>;
            if (delta == null) continue;

            if (delta.ContainsKey("content")) {
                string c = delta["content"] as string;
                if (!string.IsNullOrEmpty(c)) sb.Append(c);
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    static void AppendStreamPiece(StringBuilder sb, string piece)
    {
        if (string.IsNullOrEmpty(piece)) return;
        string current = sb.ToString();
        if (current.Length == 0) { sb.Append(piece); return; }
        if (current == piece) return;

        string curNoWs = NormalizeNoSpaces(current);
        string pieceNoWs = NormalizeNoSpaces(piece);

        if (curNoWs.Length > 0 && pieceNoWs.Length > 0 &&
            pieceNoWs.IndexOf(curNoWs, StringComparison.Ordinal) >= 0 &&
            piece.Length > current.Length) {
            sb.Length = 0;
            sb.Append(piece);
            return;
        }

        sb.Append(piece);
    }

    static string NormalizeNoSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    static string ParseFallback(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string trimmed = raw.TrimStart();

        if (trimmed.StartsWith("{")) {
            try {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                object obj = ser.DeserializeObject(trimmed);
                string phase = FindJsonString(obj, "phase", 0);
                if (!IsThinkingPhase(phase)) {
                    string t = ExtractBestText(obj);
                    if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
                }
            } catch { }
        }

        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("<") && !trimmed.Contains("data:"))
            return trimmed.Trim();

        return null;
    }

    // ══════════════════════════════════════════════
    //  JSON TRAVERSAL HELPERS
    // ══════════════════════════════════════════════
    static string FindJsonString(object node, string key, int depth)
    {
        if (node == null || depth > 12) return null;

        var dict = node as Dictionary<string, object>;
        if (dict != null) {
            if (dict.ContainsKey(key)) {
                string s = dict[key] as string;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            foreach (var kv in dict) {
                string s = FindJsonString(kv.Value, key, depth + 1);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return null;
        }

        var arr = node as object[];
        if (arr != null)
            foreach (object el in arr) {
                string s = FindJsonString(el, key, depth + 1);
                if (!string.IsNullOrEmpty(s)) return s;
            }

        return null;
    }

    static bool IsThinkingPhase(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase)) return false;
        string p = phase.ToLowerInvariant();
        return p.Contains("think") || p.Contains("reason") || p.Contains("reflection") || p.Contains("thought");
    }

    static string ExtractBestText(object node)
    {
        var candidates = new List<string>();
        CollectBestCandidates(node, candidates, 0);

        string best = null; int bestLen = -1;
        foreach (string c in candidates)
            if (c != null && c.Length > bestLen) { best = c; bestLen = c.Length; }

        return best;
    }

    static void CollectBestCandidates(object node, List<string> candidates, int depth)
    {
        if (node == null || depth > 16) return;

        string s = node as string;
        if (s != null) { candidates.Add(s); return; }

        var dict = node as Dictionary<string, object>;
        if (dict != null) {
            if (dict.ContainsKey("phase")) {
                string ph = dict["phase"] as string;
                if (IsThinkingPhase(ph)) return;
            }

            string[] keys = { "content", "text", "message", "delta", "choices", "content_list", "output", "result", "response", "answer" };
            foreach (string key in keys) {
                if (!dict.ContainsKey(key)) continue;
                CollectBestCandidates(dict[key], candidates, depth + 1);
            }
            return;
        }

        var arr = node as object[];
        if (arr != null)
            foreach (object el in arr)
                CollectBestCandidates(el, candidates, depth + 1);
    }

    static string UnescapeJson(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++) {
            if (s[i] == '\\' && i + 1 < s.Length) {
                char next = s[i + 1];
                switch (next) {
                    case '"':  sb.Append('"');  i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '/':  sb.Append('/');  i++; break;
                    case 'n':  sb.Append('\n'); i++; break;
                    case 'r':  sb.Append('\r'); i++; break;
                    case 't':  sb.Append('\t'); i++; break;
                    case 'u':
                        if (i + 5 < s.Length) {
                            int code;
                            if (int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out code)) {
                                sb.Append((char)code); i += 5;
                            } else sb.Append(s[i]);
                        } else sb.Append(s[i]);
                        break;
                    default: sb.Append(s[i]); break;
                }
            } else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════
    //  PAUSE HELPERS
    // ══════════════════════════════════════════════
    static void PauseBeforePrimary(string label)
    {
        if (StopRequested) return;
        WriteColored(ConsoleColor.DarkGray, "  \u25CC Пауза перед Primary (анти-WAF)...\n");
        SleepInterruptible(PrimaryPauseAfterDispatchMs);
    }

    static void PauseBetweenRoles()
    {
        if (!StopRequested) Thread.Sleep(RolePauseMs);
    }
}