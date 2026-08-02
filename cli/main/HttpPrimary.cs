// HttpPrimary.cs — PostMessage (primary), PostMessageWithTimeout, таймауты, retry
// New Era CLI v6.0 · partial class MainConsole
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

partial class MainConsole
{
    static string PrimaryModel = "qwen3.8-max-preview";
    static string QwenVersion = "0.2.66";

    const int MaxAttempts = 10;
    const int RetryDelayMs = 2000;

    const int PrimaryTimeoutMs = 120000;
    const int PrimaryReadWriteTimeoutMs = 180000;

    const int CodeWriterTimeoutMs = 300000;
    const int CodeWriterReadWriteTimeoutMs = 600000;

    static string PostMessage(string text, string parentId)
    {
        return PostMessage(text, parentId, PrimaryModel, ApiBaseUrl);
    }

    static string PostMessage(string text, string parentId, string model, string apiBase)
    {
        string effectiveModel = string.IsNullOrEmpty(model) ? PrimaryModel : model;
        string effectiveBase = string.IsNullOrEmpty(apiBase) ? ApiBaseUrl : apiBase;

        string url = effectiveBase.TrimEnd('/') + "/api/v2/chat/completions";
        if (!string.IsNullOrEmpty(ChatId))
            url += "?chat_id=" + Uri.EscapeDataString(ChatId);

        int ts = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        string msgId = Guid.NewGuid().ToString();

        var sb = new StringBuilder();

        sb.Append(
            "{\"stream\":true,\"version\":\"3\",\"incremental_output\":true,\"chat_id\":" + JsonStr(ChatId) +
            ",\"chat_mode\":\"normal\",\"model\":" + JsonStr(effectiveModel) +
            ",\"parent_id\":" + JsonStr(parentId) + ",\"messages\":[{\"fid\":\"" + msgId +
            "\",\"parentId\":" + JsonStr(parentId) +
            ",\"childrenIds\":[],\"role\":\"user\",\"content\":" + EscapeJson(text) +
            ",\"user_action\":\"chat\",\"files\":[],\"timestamp\":" + ts +
            ",\"models\":[" + JsonStr(effectiveModel) +
            "],\"chat_type\":\"t2t\",\"feature_config\":{\"thinking_enabled\":true,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"Thinking\",\"thinking_format\":\"summary\",\"auto_search\":true},\"extra\":{\"meta\":{\"subChatType\":\"t2t\"}},\"sub_chat_type\":\"t2t\"}],\"timestamp\":" + (ts + 1) + "}");

        byte[] bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        Exception lastEx = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                if (StopRequested)
                    break;

                Thread.Sleep(RetryDelayMs * attempt);
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);

            req.Method = "POST";
            req.Timeout = PrimaryTimeoutMs;
            req.ReadWriteTimeout = PrimaryReadWriteTimeoutMs;

            req.ContentType = "application/json";
            req.Accept = "application/json, text/plain, */*";

            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = false;

            ApplyAuth(req, effectiveBase);

            req.Headers["source"] = "web";
            req.Headers["Origin"] = "https://chat.qwen.ai";
            req.Referer = "https://chat.qwen.ai/c/" + (ChatId ?? "");

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
                catch
                {
                }
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
                        throw new Exception("HTTP " + code + " — Aliyun WAF / капча. Нужны cookie из браузера.");

                    throw new Exception("HTTP " + code + " — сервер вернул HTML.");
                }

                if (code == 401 || code == 403)
                    throw new Exception("HTTP " + code + " — токен недействителен");

                if (code == 429 || code >= 500)
                {
                    lastEx = new Exception("HTTP " + code);
                    if (attempt + 1 < MaxAttempts)
                        continue;
                }

                string head = (errBody ?? "").Replace("\r", " ").Replace("\n", " ");
                if (head.Length > 300)
                    head = head.Substring(0, 300) + "...";

                throw new Exception("HTTP " + code + " " + head);
            }

            string trimmed = (response ?? "").TrimStart();

            if (trimmed.Length == 0)
            {
                if (attempt + 1 < MaxAttempts)
                {
                    lastEx = new Exception("сервер вернул пустое тело");
                    continue;
                }

                throw new Exception("сервер вернул пустое тело");
            }

            if (trimmed.StartsWith("<"))
            {
                if (trimmed.Contains("aliyun_waf") || trimmed.Contains("AliyunCaptcha"))
                    throw new Exception("сервер вернул Aliyun WAF / капчу. Нужны cookie из браузера.");

                throw new Exception("сервер вернул HTML, а не SSE.");
            }

            if (trimmed.StartsWith("{") && trimmed.Contains("\"error\""))
            {
                string snippet = trimmed.Length > 400 ? trimmed.Substring(0, 400) + "..." : trimmed;
                throw new Exception("API: " + snippet);
            }

            return response;
        }

        throw lastEx ?? new Exception("не удалось отправить запрос");
    }

    static string PostMessageWithTimeout(
        string text,
        string parentId,
        string model,
        string apiBase,
        int timeoutMs,
        int rwTimeoutMs)
    {
        string effectiveModel = string.IsNullOrEmpty(model) ? PrimaryModel : model;
        string effectiveBase = string.IsNullOrEmpty(apiBase) ? ApiBaseUrl : apiBase;

        string url = effectiveBase.TrimEnd('/') + "/api/v2/chat/completions";
        if (!string.IsNullOrEmpty(ChatId))
            url += "?chat_id=" + Uri.EscapeDataString(ChatId);

        int ts = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        string msgId = Guid.NewGuid().ToString();

        var sb = new StringBuilder();

        sb.Append(
            "{\"stream\":true,\"version\":\"3\",\"incremental_output\":true,\"chat_id\":" + JsonStr(ChatId) +
            ",\"chat_mode\":\"normal\",\"model\":" + JsonStr(effectiveModel) +
            ",\"parent_id\":" + JsonStr(parentId) + ",\"messages\":[{\"fid\":\"" + msgId +
            "\",\"parentId\":" + JsonStr(parentId) +
            ",\"childrenIds\":[],\"role\":\"user\",\"content\":" + EscapeJson(text) +
            ",\"user_action\":\"chat\",\"files\":[],\"timestamp\":" + ts +
            ",\"models\":[" + JsonStr(effectiveModel) +
            "],\"chat_type\":\"t2t\",\"feature_config\":{\"thinking_enabled\":true,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"Thinking\",\"thinking_format\":\"summary\",\"auto_search\":true},\"extra\":{\"meta\":{\"subChatType\":\"t2t\"}},\"sub_chat_type\":\"t2t\"}],\"timestamp\":" + (ts + 1) + "}");

        byte[] bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        Exception lastEx = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                if (StopRequested)
                    break;

                Thread.Sleep(RetryDelayMs * attempt);
            }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);

            req.Method = "POST";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = rwTimeoutMs;

            req.ContentType = "application/json";
            req.Accept = "application/json, text/plain, */*";

            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            req.KeepAlive = false;

            ApplyAuth(req, effectiveBase);

            req.Headers["source"] = "web";
            req.Headers["Origin"] = "https://chat.qwen.ai";
            req.Referer = "https://chat.qwen.ai/c/" + (ChatId ?? "");

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
                catch
                {
                }
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
                        throw new Exception("HTTP " + code + " — Aliyun WAF / капча.");

                    throw new Exception("HTTP " + code + " — сервер вернул HTML.");
                }

                if (code == 401 || code == 403)
                    throw new Exception("HTTP " + code + " — токен недействителен");

                if (code == 429 || code >= 500)
                {
                    lastEx = new Exception("HTTP " + code);
                    if (attempt + 1 < MaxAttempts)
                        continue;
                }

                string head = (errBody ?? "").Replace("\r", " ").Replace("\n", " ");
                if (head.Length > 300)
                    head = head.Substring(0, 300) + "...";

                throw new Exception("HTTP " + code + " " + head);
            }

            string trimmed = (response ?? "").TrimStart();

            if (trimmed.Length == 0)
            {
                if (attempt + 1 < MaxAttempts)
                {
                    lastEx = new Exception("сервер вернул пустое тело");
                    continue;
                }

                throw new Exception("сервер вернул пустое тело");
            }

            if (trimmed.StartsWith("<"))
                throw new Exception("сервер вернул HTML, а не SSE.");

            if (trimmed.StartsWith("{") && trimmed.Contains("\"error\""))
            {
                string snippet = trimmed.Length > 400 ? trimmed.Substring(0, 400) + "..." : trimmed;
                throw new Exception("API: " + snippet);
            }

            return response;
        }

        throw lastEx ?? new Exception("не удалось отправить запрос");
    }
}