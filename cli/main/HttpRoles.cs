// HttpRoles.cs — HTTP для ролей AI #2: dispatcher, extractor, validator, test
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// FIX v6.0 (parent_id): parent_id БОЛЬШЕ не null. Берётся из трекера
// нужного чата (LastResponseId для Primary, LastAi2ResponseId для AI #2)
// и после ответа обновляется из response_id. Так /test и роли пишут
// НОВОЕ сообщение в конец чата, а не перегенерируют первое.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
partial class MainConsole
{
const int RoleMaxRetries   = 1;
const int RoleRetryDelayMs = 1500;
// ══════════════════════════════════════════════════════════
//  POST ROLE CHAT MESSAGE
// ══════════════════════════════════════════════════════════
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
string fullPrompt = string.IsNullOrEmpty(systemPrompt)
? userPrompt
: systemPrompt + "\n" + userPrompt;
bool isPrimaryChat = token == Token && effectiveChat == ChatId;
// parent_id цепочки: свой трекер на каждый чат.
string parentId = isPrimaryChat ? LastResponseId : LastAi2ResponseId;
bool thinking = isPrimaryChat || ShouldUseThinkingForRole(effectiveModel);
string featureConfig = thinking
? "\"feature_config\":{\"thinking_enabled\":true,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"Thinking\",\"thinking_format\":\"summary\",\"auto_search\":true}"
: "\"feature_config\":{\"thinking_enabled\":false,\"output_schema\":\"phase\",\"research_mode\":\"normal\",\"auto_thinking\":false,\"thinking_mode\":\"None\",\"thinking_format\":\"summary\",\"auto_search\":false}";
string url = effectiveBase.TrimEnd('/') + "/api/v2/chat/completions";
url += "?chat_id=" + Uri.EscapeDataString(effectiveChat);
int ts = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
string msgId = Guid.NewGuid().ToString();
var sb = new StringBuilder();
sb.Append(
"{\"stream\":true,\"version\":\"3\",\"incremental_output\":true,\"chat_id\":" + JsonStr(effectiveChat) +
",\"chat_mode\":\"normal\",\"model\":" + JsonStr(effectiveModel) +
",\"parent_id\":" + JsonStr(parentId) + ",\"messages\":[{\"fid\":\"" + msgId +
"\",\"parentId\":" + JsonStr(parentId) + ",\"childrenIds\":[],\"role\":\"user\",\"content\":" + EscapeJson(fullPrompt) +
",\"user_action\":\"chat\",\"files\":[],\"timestamp\":" + ts +
",\"models\":[" + JsonStr(effectiveModel) +
"],\"chat_type\":\"t2t\"," + featureConfig +
",\"extra\":{\"meta\":{\"subChatType\":\"t2t\"}},\"sub_chat_type\":\"t2t\"}],\"timestamp\":" + (ts + 1) + "}"
);
byte[] bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
Exception lastEx = null;
for (int attempt = 0; attempt <= RoleMaxRetries; attempt++)
{
if (attempt > 0)
{
if (StopRequested)
break;
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
if (attempt < RoleMaxRetries)
continue;
throw lastEx;
}
string head = (errBody ?? "").Replace("\r", " ").Replace("\n", " ");
if (head.Length > 200)
head = head.Substring(0, 200) + "...";
throw new Exception(roleName + " HTTP " + code + " " + head);
}
try
{
File.WriteAllText(DumpFile, response ?? "", new UTF8Encoding(false));
}
catch { }
string trimmed = (response ?? "").TrimStart();
if (trimmed.Length == 0)
{
lastEx = new Exception(roleName + ": сервер вернул пустое тело");
if (attempt < RoleMaxRetries)
continue;
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
string snippet = trimmed.Length > 400
? trimmed.Substring(0, 400) + "..."
: trimmed;
throw new Exception(roleName + " API: " + snippet);
}
// Парсим и ОБНОВЛЯЕМ цепочку parent_id нужного чата.
Action<string> parentSetter = isPrimaryChat
? new Action<string>(SetPrimaryParent)
: new Action<string>(SetAi2Parent);
string parsed = ParseSseAnswerEx(response, parentSetter);
if (string.IsNullOrWhiteSpace(parsed))
parsed = ParseOrchestratorResponse(response);
// ParseOrchestratorResponse не знает про сеттер — вытянем
// response_id вручную, чтобы цепочка не порвалась.
if (!string.IsNullOrWhiteSpace(parsed))
{
Match rid = Regex.Match(response, "\"response_id\"\\s*:\\s*\"([^\"]+)\"");
if (rid.Success)
parentSetter(rid.Groups[1].Value);
}
if (string.IsNullOrWhiteSpace(parsed))
{
string apiErr = TryExtractApiErrorMessage(response);
if (!string.IsNullOrEmpty(apiErr))
throw new Exception(roleName + " API: " + apiErr);
lastEx = new Exception(roleName + ": ответ получен, но текст не извлечён");
if (attempt < RoleMaxRetries)
continue;
throw lastEx;
}
return parsed;
}
throw lastEx ?? new Exception(roleName + ": не удалось отправить запрос");
}
// ══════════════════════════════════════════════════════════
//  THINKING MODE
// ══════════════════════════════════════════════════════════
static bool ShouldUseThinkingForRole(string model)
{
string m = (model ?? "").ToLowerInvariant();
string p = (PrimaryModel ?? "").ToLowerInvariant();
if (m.Length == 0)
return true;
if (m == p)
return true;
if (m.Contains("3.8"))
return true;
if (m.Contains("preview"))
return true;
return false;
}
// ══════════════════════════════════════════════════════════
//  ERROR MESSAGE EXTRACTION
// ══════════════════════════════════════════════════════════
static string TryExtractApiErrorMessage(string raw)
{
if (string.IsNullOrEmpty(raw))
return null;
if (!raw.Contains("\"error\""))
return null;
Match m = Regex.Match(raw, "\"message\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
if (m.Success)
return UnescapeJson(m.Groups[1].Value);
return null;
}
// ══════════════════════════════════════════════════════════
//  AUTH
// ══════════════════════════════════════════════════════════
static void ApplyAuth(HttpWebRequest req)
{
ApplyAuth(req, ApiBaseUrl);
}
static void ApplyAuth(HttpWebRequest req, string apiBase)
{
if (string.IsNullOrEmpty(Token))
return;
req.Headers[HttpRequestHeader.Authorization] = "Bearer " + Token;
string cookieValue = !string.IsNullOrEmpty(CookieHeader)
? CookieHeader
: ("token=" + Token);
if (!string.IsNullOrEmpty(CookieHeader))
{
try
{
req.Headers[HttpRequestHeader.Cookie] = cookieValue;
return;
}
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
if (string.IsNullOrEmpty(token))
return;
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
}