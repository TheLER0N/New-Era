// HttpRoles.cs — PostOrchestratorMessage, PostGuardianMessage, PostCodeWriterMessage, ApplyAuth*
// New Era CLI v5.3 · partial class MainConsole
//
// v5.3:
//   - Добавлен универсальный PostRoleChatMessage().
//   - Orchestrator/Guardian теперь используют streaming-запрос и отдельный chat_id.
//   - Orchestrator больше не теряет AI#2.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

partial class MainConsole
{
    const int OrchTimeoutMs = 90000;
    const int OrchReadWriteTimeoutMs = 120000;

    const int GuardianTimeoutMs = 60000;
    const int GuardianReadWriteTimeoutMs = 90000;

    const int RoleMaxRetries = 1;
    const int RoleRetryDelayMs = 1500;

    static string PostOrchestratorMessage(string systemPrompt, string userPrompt)
    {
        string orchBase  = string.IsNullOrEmpty(OrchestratorApiUrl) ? ApiBaseUrl : OrchestratorApiUrl;
        string orchModel = string.IsNullOrEmpty(OrchestratorModel) ? PrimaryModel : OrchestratorModel;
        string orchToken = string.IsNullOrEmpty(OrchestratorToken) ? Token : OrchestratorToken;
        string orchChat  = string.IsNullOrEmpty(OrchestratorChatId) ? ChatId : OrchestratorChatId;

        return PostRoleChatMessage(
            "Orchestrator",
            systemPrompt,
            userPrompt,
            orchModel,
            orchBase,
            orchToken,
            orchChat,
            OrchTimeoutMs,
            OrchReadWriteTimeoutMs
        );
    }

    static string PostGuardianMessage(string systemPrompt, string userPrompt)
    {
        string guardBase  = string.IsNullOrEmpty(GuardianApiUrl) ? ApiBaseUrl : GuardianApiUrl;
        string guardModel = string.IsNullOrEmpty(GuardianModel) ? PrimaryModel : GuardianModel;
        string guardToken = string.IsNullOrEmpty(GuardianToken) ? Token : GuardianToken;

        string guardChat = ChatId;
        if (!string.IsNullOrEmpty(GuardianToken) && GuardianToken == Token2 && !string.IsNullOrEmpty(ChatId2))
            guardChat = ChatId2;

        return PostRoleChatMessage(
            "Guardian",
            systemPrompt,
            userPrompt,
            guardModel,
            guardBase,
            guardToken,
            guardChat,
            GuardianTimeoutMs,
            GuardianReadWriteTimeoutMs
        );
    }

    static string PostRoleChatMessage(
        string roleName,
        string systemPrompt,
        string userPrompt,
        string model,
        string apiBase,
        string token,
        string chatId,
        int timeoutMs,
        int rwTimeoutMs)
    {
        if (string.IsNullOrEmpty(userPrompt))
            throw new Exception(roleName + ": пустой запрос");

        if (string.IsNullOrEmpty(token))
            throw new Exception(roleName + ": нет токена");

        string effectiveModel = string.IsNullOrEmpty(model) ? PrimaryModel : model;
        string effectiveBase  = string.IsNullOrEmpty(apiBase) ? ApiBaseUrl : apiBase;
        string effectiveChat  = chatId;

        if (string.IsNullOrEmpty(effectiveChat))
            throw new Exception(roleName + ": нет chat_id");

        string url = effectiveBase.TrimEnd('/') + "/api/v2/chat/completions";
        if (!string.IsNullOrEmpty(effectiveChat))
            url += "?chat_id=" + Uri.EscapeDataString(effectiveChat);

        int ts = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        string msgId = Guid.NewGuid().ToString();

        var sb = new StringBuilder();

        sb.Append(
            "{\"stream\":true,\"version\":\"3\",\"incremental_output\":true,\"chat_id\":" + JsonStr(effectiveChat) +
            ",\"chat_mode\":\"normal\",\"model\":" + JsonStr(effectiveModel) +
            ",\"parent_id\":null,\"messages\":["
        );

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            sb.Append(
                "{\"fid\":\"" + Guid.NewGuid().ToString() + "\",\"parentId\":null,\"childrenIds\":[],\"role\":\"system\",\"content\":" + EscapeJson(systemPrompt) +
                ",\"user_action\":\"chat\",\"files\":[],\"timestamp\":" + ts +
                ",\"models\":[" + JsonStr(effectiveModel) +
                "],\"chat_type\":\"t2t\",\"feature_config\":{\"thinking_enabled\":false,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"None\",\"thinking_format\":\"summary\",\"auto_search\":false},\"extra\":{\"meta\":{\"subChatType\":\"t2t\"}},\"sub_chat_type\":\"t2t\"},"
            );
        }

        sb.Append(
            "{\"fid\":\"" + msgId + "\",\"parentId\":null,\"childrenIds\":[],\"role\":\"user\",\"content\":" + EscapeJson(userPrompt) +
            ",\"user_action\":\"chat\",\"files\":[],\"timestamp\":" + (ts + 1) +
            ",\"models\":[" + JsonStr(effectiveModel) +
            "],\"chat_type\":\"t2t\",\"feature_config\":{\"thinking_enabled\":false,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"None\",\"thinking_format\":\"summary\",\"auto_search\":false},\"extra\":{\"meta\":{\"subChatType\":\"t2t\"}},\"sub_chat_type\":\"t2t\"}],\"timestamp\":" + (ts + 2) + "}"
        );

        byte[] bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());

        Exception lastEx = null;

        for (int attempt = 0; attempt <= RoleMaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                if (StopRequested) break;
                Thread.Sleep(RoleRetryDelayMs * attempt);
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);

            req.Method = "POST";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = rwTimeoutMs;

            req.ContentType = "application/json";
            req.Accept = "application/json, text/plain, */*";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = false;

            ApplyAuthForRole(req, effectiveBase, token);

            req.Headers["source"] = "web";
            req.Headers["Origin"] = "https://chat.qwen.ai";
            req.Referer = "https://chat.qwen.ai/c/" + (effectiveChat ?? "");

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

            try
            {
                using (Stream rs = req.GetRequestStream())
                    rs.Write(bodyBytes, 0, bodyBytes.Length);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                continue;
            }

            string response;

            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    response = reader.ReadToEnd();
            }
            catch (WebException wex)
            {
                HttpWebResponse r = wex.Response as HttpWebResponse;

                int code = -1;
                string errBody = "";

                try
                {
                    if (r != null)
                    {
                        code = (int)r.StatusCode;

                        using (Stream es = r.GetResponseStream())
                        using (StreamReader er = new StreamReader(es, Encoding.UTF8))
                            errBody = er.ReadToEnd();
                    }
                }
                catch { }
                finally
                {
                    if (r != null)
                    {
                        try { r.Close(); } catch { }
                    }
                }

                string trimmedErr = (errBody ?? "").TrimStart();

                if (trimmedErr.StartsWith("<"))
                {
                    if (trimmedErr.Contains("aliyun_waf") || trimmedErr.Contains("AliyunCaptcha"))
                        throw new Exception(roleName + " HTTP " + code + " — Aliyun WAF / капча.");

                    throw new Exception(roleName + " HTTP " + code + " — сервер вернул HTML.");
                }

                if (code == 401 || code == 403)
                    throw new Exception(roleName + " HTTP " + code + " — токен недействителен");

                if (code == 429 || code >= 500 || code == -1)
                {
                    lastEx = new Exception(roleName + " HTTP " + code);
                    if (attempt < RoleMaxRetries) continue;
                    throw lastEx;
                }

                string head = (errBody ?? "").Replace("\r", " ").Replace("\n", " ");
                if (head.Length > 200) head = head.Substring(0, 200) + "...";

                throw new Exception(roleName + " HTTP " + code + " " + head);
            }

            string trimmed = (response ?? "").TrimStart();

            if (trimmed.Length == 0)
            {
                lastEx = new Exception(roleName + ": сервер вернул пустое тело");
                if (attempt < RoleMaxRetries) continue;
                throw lastEx;
            }

            if (trimmed.StartsWith("<"))
            {
                if (trimmed.Contains("aliyun_waf") || trimmed.Contains("AliyunCaptcha"))
                    throw new Exception(roleName + ": сервер вернул Aliyun WAF / капчу.");

                throw new Exception(roleName + ": сервер вернул HTML, а не ответ API.");
            }

            if (trimmed.StartsWith("{") && trimmed.Contains("\"error\""))
            {
                string snippet = trimmed.Length > 400 ? trimmed.Substring(0, 400) + "..." : trimmed;
                throw new Exception(roleName + " API: " + snippet);
            }

            string parsed = ParseRoleAnswer(response);
            if (string.IsNullOrWhiteSpace(parsed))
                parsed = ParseOrchestratorResponse(response);

            if (string.IsNullOrWhiteSpace(parsed))
            {
                lastEx = new Exception(roleName + ": ответ получен, но текст не извлечён");
                if (attempt < RoleMaxRetries) continue;
                throw lastEx;
            }

            return parsed;
        }

        throw lastEx ?? new Exception(roleName + ": не удалось отправить запрос");
    }

    static string PostCodeWriterMessage(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
            throw new Exception("CodeWriter: пустой промпт");

        WriteColored(ConsoleColor.DarkGray, "    [CW-http] Отправка: " + prompt.Length + " символов\n");

        string raw = PostMessageWithTimeout(
            prompt,
            LastResponseId,
            PrimaryModel,
            ApiBaseUrl,
            CodeWriterTimeoutMs,
            CodeWriterReadWriteTimeoutMs
        );

        string result = ParseSseAnswer(raw);

        WriteColored(ConsoleColor.DarkGray,
            "    [CW-http] Получено: " + (raw != null ? raw.Length : 0) + " raw, " +
            (result != null ? result.Length : 0) + " parsed\n");

        try { File.WriteAllText(DumpFile, raw ?? "", new UTF8Encoding(false)); } catch { }

        return result;
    }

    static string PostCodeWriterMessage(string prompt, string model, string apiBase)
    {
        if (string.IsNullOrEmpty(prompt))
            throw new Exception("CodeWriter: пустой промпт");

        string effectiveModel = string.IsNullOrEmpty(model) ? PrimaryModel : model;
        string effectiveBase  = string.IsNullOrEmpty(apiBase) ? ApiBaseUrl : apiBase;

        string raw = PostMessage(prompt, LastResponseId, effectiveModel, effectiveBase);
        string result = ParseSseAnswer(raw);

        try { File.WriteAllText(DumpFile, raw ?? "", new UTF8Encoding(false)); } catch { }

        return result;
    }

    static void ApplyAuth(HttpWebRequest req)
    {
        ApplyAuth(req, ApiBaseUrl);
    }

    static void ApplyAuth(HttpWebRequest req, string apiBase)
    {
        if (string.IsNullOrEmpty(Token)) return;

        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + Token;

        string cookieValue = !string.IsNullOrEmpty(CookieHeader) ? CookieHeader : ("token=" + Token);

        if (!string.IsNullOrEmpty(CookieHeader))
        {
            try { req.Headers[HttpRequestHeader.Cookie] = cookieValue; return; }
            catch { }
        }

        try
        {
            var cc = new CookieContainer();
            cc.SetCookies(new Uri(apiBase), cookieValue);
            req.CookieContainer = cc;
        }
        catch
        {
            try { req.Headers[HttpRequestHeader.Cookie] = cookieValue; }
            catch { }
        }
    }

    static void ApplyAuthForRole(HttpWebRequest req, string apiBase, string token)
    {
        if (string.IsNullOrEmpty(token)) return;

        // Если это основной токен и есть браузерные cookie — используем их.
        if (token == Token && !string.IsNullOrEmpty(CookieHeader))
        {
            ApplyAuth(req, apiBase);
            return;
        }

        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;

        string cookieValue = "token=" + token;

        try
        {
            var cc = new CookieContainer();
            cc.SetCookies(new Uri(apiBase), cookieValue);
            req.CookieContainer = cc;
        }
        catch
        {
            try { req.Headers[HttpRequestHeader.Cookie] = cookieValue; }
            catch { }
        }
    }

    static void ApplyAuthForOrchestrator(HttpWebRequest req, string apiBase, string orchToken)
    {
        string effectiveToken = string.IsNullOrEmpty(orchToken) ? Token : orchToken;
        if (string.IsNullOrEmpty(effectiveToken)) return;

        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + effectiveToken;

        string cookieValue = "token=" + effectiveToken;

        try
        {
            var cc = new CookieContainer();
            cc.SetCookies(new Uri(apiBase), cookieValue);
            req.CookieContainer = cc;
        }
        catch
        {
            try { req.Headers[HttpRequestHeader.Cookie] = cookieValue; }
            catch { }
        }
    }

    static void ApplyAuthForGuardian(HttpWebRequest req, string apiBase, string guardToken)
    {
        string effectiveToken = string.IsNullOrEmpty(guardToken) ? Token : guardToken;
        if (string.IsNullOrEmpty(effectiveToken)) return;

        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + effectiveToken;

        if (!string.IsNullOrEmpty(CookieHeader))
        {
            try { req.Headers[HttpRequestHeader.Cookie] = CookieHeader; return; }
            catch { }
        }

        string cookieValue = "token=" + effectiveToken;

        try
        {
            var cc = new CookieContainer();
            cc.SetCookies(new Uri(apiBase), cookieValue);
            req.CookieContainer = cc;
        }
        catch
        {
            try { req.Headers[HttpRequestHeader.Cookie] = cookieValue; }
            catch { }
        }
    }
}