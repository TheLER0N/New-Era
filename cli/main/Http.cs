// Http.cs — парсинг ответов (SSE, orchestrator, JSON)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x  (ВАЖНО: без out _ / без ?. / без $"")
//
// FIX v6.0: Qwen шлёт ответ инкрементально как
//   data: {"choices":[{"delta":{"content":"кусок","phase":"answer"}}]}
// ExtractSseText идёт по choices→delta→content и склеивает ВСЕ куски
// по порядку, игнорируя фазы мышления (think/reason/summary/reflection).
//
// FIX v6.0 (parent_id): парсер умеет обновлять ПЕРЕДАННЫЙ трекер
// parent_id через Action<string>, чтобы и Primary, и AI #2 вели свою
// цепочку и каждое обращение писало НОВОЕ сообщение в конец чата.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
partial class MainConsole
{
// ══════════════════════════════════════════════════════════
//  PARENT_ID SETTERS
// ══════════════════════════════════════════════════════════
static void SetPrimaryParent(string id)
{
if (!string.IsNullOrEmpty(id)) LastResponseId = id;
}
static void SetAi2Parent(string id)
{
if (!string.IsNullOrEmpty(id)) LastAi2ResponseId = id;
}
// ══════════════════════════════════════════════════════════
//  ORCHESTRATOR / NON-STREAMING RESPONSE PARSER
// ══════════════════════════════════════════════════════════
static string ParseOrchestratorResponse(string raw)
{
if (string.IsNullOrEmpty(raw))
return null;
string trimmed = raw.TrimStart();
if (trimmed.StartsWith("{"))
{
try
{
var ser = new JavaScriptSerializer();
ser.MaxJsonLength = int.MaxValue;
var obj = ser.DeserializeObject(trimmed) as Dictionary<string, object>;
if (obj != null)
{
string t = ExtractSseText(obj);
if (!string.IsNullOrWhiteSpace(t))
return t.Trim();
}
}
catch
{
}
}
if (trimmed.Contains("data:"))
return ParseSseAnswerEx(raw, null);
return RegexGlueAll(raw);
}
// ══════════════════════════════════════════════════════════
//  SSE PARSER (обёртки)
// ══════════════════════════════════════════════════════════
static string ParseSseAnswer(string raw)
{
return ParseSseAnswerEx(raw, new Action<string>(SetPrimaryParent));
}
static string ParseSseAnswer(string raw, bool updateParent)
{
return ParseSseAnswerEx(
raw,
updateParent ? new Action<string>(SetPrimaryParent) : null);
}
static string ParseRoleAnswer(string raw)
{
return ParseSseAnswerEx(raw, null);
}
// ══════════════════════════════════════════════════════════
//  SSE PARSER (ядро)
//  parentSetter != null  →  в конце пишем response_id в нужный трекер.
// ══════════════════════════════════════════════════════════
static string ParseSseAnswerEx(string raw, Action<string> parentSetter)
{
if (string.IsNullOrEmpty(raw))
return null;
var parts = new StringBuilder();
string newResponseId = null;
var ser = new JavaScriptSerializer();
ser.MaxJsonLength = int.MaxValue;
string[] lines = raw.Split(new[] { "\n" }, StringSplitOptions.None);
foreach (string rawLine in lines)
{
string t = rawLine.Trim();
if (!t.StartsWith("data:"))
continue;
string data = t.Substring(5).Trim();
if (data.Length == 0)
continue;
if (data == "[DONE]")
break;
if (data.Contains("response_id"))
{
Match rid = Regex.Match(data, "\"response_id\"\\s*:\\s*\"([^\"]+)\"");
if (rid.Success)
newResponseId = rid.Groups[1].Value;
}
bool isCompleted =
data.Contains("response.completed") ||
data.Contains("\"status\":\"completed\"") ||
data.Contains("\"status\": \"completed\"") ||
data.Contains("\"status\":\"finished\"") ||
data.Contains("\"status\": \"finished\"");
string piece = null;
try
{
object obj = ser.DeserializeObject(data);
piece = ExtractSseText(obj);
}
catch
{
if (!Regex.IsMatch(
data,
"\"phase\"\\s*:\\s*\"[^\"]*(think|thinking|reason|summary|reflection)[^\"]*\""))
{
Match cm = Regex.Match(
data,
"\"(?:content|delta|text)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
if (cm.Success)
piece = UnescapeJson(cm.Groups[1].Value);
}
}
if (!string.IsNullOrEmpty(piece))
{
if (isCompleted && parts.Length > 0)
{
// skip duplicate final content
}
else
{
parts.Append(piece);
}
}
}
if (!string.IsNullOrEmpty(newResponseId) && parentSetter != null)
parentSetter(newResponseId);
string result = parts.ToString();
if (result != null)
result = result.Trim();
if (string.IsNullOrEmpty(result))
{
string trimmed = raw.TrimStart();
if (trimmed.StartsWith("{"))
{
try
{
var ser2 = new JavaScriptSerializer();
ser2.MaxJsonLength = int.MaxValue;
var obj = ser2.DeserializeObject(trimmed) as Dictionary<string, object>;
if (obj != null)
{
string t = ExtractSseText(obj);
if (!string.IsNullOrWhiteSpace(t))
result = t.Trim();
}
}
catch
{
}
}
}
if (string.IsNullOrEmpty(result))
result = RegexGlueAll(raw);
return result;
}
// Склеивает все content/delta/text из сырого текста, пропуская фазы мышления.
static string RegexGlueAll(string raw)
{
if (string.IsNullOrEmpty(raw))
return null;
var pieces = new List<string>();
string[] lines = raw.Split(new[] { "\n" }, StringSplitOptions.None);
foreach (string rawLine in lines)
{
string line = rawLine.Trim();
if (line.Length == 0)
continue;
if (Regex.IsMatch(
line,
"\"phase\"\\s*:\\s*\"[^\"]*(think|thinking|reason|summary|reflection)[^\"]*\""))
continue;
foreach (Match cm in Regex.Matches(
line,
"\"(?:content|delta|text)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
{
string part = UnescapeJson(cm.Groups[1].Value);
if (!string.IsNullOrEmpty(part))
pieces.Add(part);
}
}
if (pieces.Count == 0)
{
foreach (Match cm in Regex.Matches(
raw,
"\"(?:content|delta|text)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
{
string part = UnescapeJson(cm.Groups[1].Value);
if (!string.IsNullOrEmpty(part))
pieces.Add(part);
}
}
if (pieces.Count == 0)
return null;
return string.Join("", pieces.ToArray()).Trim();
}
// ══════════════════════════════════════════════════════════
//  JSON UNESCAPE
// ══════════════════════════════════════════════════════════
static string UnescapeJson(string s)
{
if (s == null)
return "";
var sb = new StringBuilder();
for (int i = 0; i < s.Length; i++)
{
if (s[i] == '\\' && i + 1 < s.Length)
{
char next = s[i + 1];
switch (next)
{
case '"':  sb.Append('"');  i++; break;
case '\\': sb.Append('\\'); i++; break;
case '/':  sb.Append('/');  i++; break;
case 'n':  sb.Append('\n'); i++; break;
case 'r':  sb.Append('\r'); i++; break;
case 't':  sb.Append('\t'); i++; break;
case 'b':  sb.Append('\b'); i++; break;
case 'f':  sb.Append('\f'); i++; break;
case 'u':
if (i + 5 < s.Length)
{
string hex = s.Substring(i + 2, 4);
int code;
if (int.TryParse(
hex,
System.Globalization.NumberStyles.HexNumber,
null,
out code))
{
sb.Append((char)code);
i += 5;
}
else
{
sb.Append(s[i]);
}
}
else
{
sb.Append(s[i]);
}
break;
default:
sb.Append(s[i]);
break;
}
}
else
{
sb.Append(s[i]);
}
}
return sb.ToString();
}
// ══════════════════════════════════════════════════════════
//  EXTRACT TEXT FROM PARSED JSON NODE
// ══════════════════════════════════════════════════════════
static string ExtractSseText(object node)
{
return ExtractSseTextCore(node, 0);
}
static string ExtractSseTextCore(object node, int depth)
{
if (node == null || depth > 16)
return null;
string s = node as string;
if (s != null)
return s;
Dictionary<string, object> dict = node as Dictionary<string, object>;
if (dict != null)
{
if (dict.ContainsKey("phase"))
{
string ph = dict["phase"] as string;
if (ph != null)
{
string pl = ph.ToLowerInvariant();
if (pl.Contains("think") ||
pl.Contains("reason") ||
pl.Contains("summary") ||
pl.Contains("reflection"))
{
return null;
}
}
}
string[] keys =
{
"choices",
"delta",
"message",
"content_list",
"content",
"text",
"output"
};
foreach (string key in keys)
{
if (dict.ContainsKey(key))
{
string t = ExtractSseTextCore(dict[key], depth + 1);
if (t != null && t.Length > 0)
return t;
}
}
return null;
}
object[] arr = node as object[];
if (arr != null)
{
var sb = new StringBuilder();
foreach (object el in arr)
{
string t = ExtractSseTextCore(el, depth + 1);
if (t != null && t.Length > 0)
{
sb.Append(t);
}
}
return sb.ToString();
}
return null;
}
}