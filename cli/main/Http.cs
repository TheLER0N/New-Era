// Http.cs — парсинг ответов (SSE, orchestrator, JSON)
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  ORCHESTRATOR RESPONSE PARSER
    // ══════════════════════════════════════════════════════════

    static string ParseOrchestratorResponse(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        string trimmed = raw.TrimStart();

        // Non-streaming: ответ может быть единый JSON
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
                    if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
                }
            }
            catch { }
        }

        // Fallback: может быть SSE
        if (trimmed.Contains("data:"))
        {
            return ParseSseAnswer(raw, false);
        }

        // Fallback 2: regex по content
        Match cm = Regex.Match(raw, @"""content""\s*:\s*""((?:\\.|[^""\\])*)""");
        if (cm.Success)
        {
            string part = UnescapeJson(cm.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(part)) return part.Trim();
        }

        return null;
    }

    // ══════════════════════════════════════════════════════════
    //  SSE PARSER
    // ══════════════════════════════════════════════════════════

    static string ParseSseAnswer(string raw)
    {
        return ParseSseAnswer(raw, true);
    }

    static string ParseRoleAnswer(string raw)
    {
        return ParseSseAnswer(raw, false);
    }

    static string ParseSseAnswer(string raw, bool updateParent)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        var parts = new StringBuilder();
        string newResponseId = null;

        var ser = new JavaScriptSerializer();
        ser.MaxJsonLength = int.MaxValue;

        string[] lines = raw.Split(new[] { "\n" }, StringSplitOptions.None);

        foreach (string rawLine in lines)
        {
            string t = rawLine.Trim();
            if (!t.StartsWith("data:")) continue;

            string data = t.Substring(5).Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            // response_id
            if (data.Contains("response.created") || data.Contains("response_id"))
            {
                Match rid = Regex.Match(data, @"""response_id""\s*:\s*""([^""]+)""");
                if (rid.Success) newResponseId = rid.Groups[1].Value;
            }

            bool isCompleted =
                data.Contains("response.completed") ||
                data.Contains("\"status\":\"completed\"") ||
                data.Contains("\"status\": \"completed\"");

            string piece = null;

            // Основной путь: десериализуем SSE-объект и вытаскиваем текст через ExtractSseText.
            // Это чинит ответы, где контент приходит не только в phase=answer,
            // а также ответы с content_list / text / message.
            try
            {
                object obj = ser.DeserializeObject(data);
                piece = ExtractSseText(obj);
            }
            catch
            {
                // Fallback, если JSON битый/неполный.
                // Не вытаскиваем thinking/reason/summary/reflection.
                if (!Regex.IsMatch(data, @"""phase""\s*:\s*""(think|thinking|reason|summary|reflection)"""))
                {
                    Match cm = Regex.Match(data, @"""content""\s*:\s*""((?:\\.|[^""\\])*)""");
                    if (cm.Success)
                        piece = UnescapeJson(cm.Groups[1].Value);
                }
            }

            if (!string.IsNullOrEmpty(piece))
            {
                // Если уже есть incremental-куски, а пришёл completed-объект с полным текстом,
                // не дублируем его.
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

        if (!string.IsNullOrEmpty(newResponseId) && updateParent)
            LastResponseId = newResponseId;

        string result = parts.ToString().Trim();

        // Fallback 1: если весь ответ — один JSON, а не SSE.
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
                        if (!string.IsNullOrWhiteSpace(t)) result = t.Trim();
                    }
                }
                catch { }
            }
        }

        // Fallback 2: последний шанс через regex.
        if (string.IsNullOrEmpty(result))
        {
            Match cm = Regex.Match(raw, @"""content""\s*:\s*""((?:\\.|[^""\\])*)""");
            if (cm.Success)
            {
                string part = UnescapeJson(cm.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(part)) result = part.Trim();
            }
        }

        return result;
    }

    static string UnescapeJson(string s)
    {
        if (s == null) return "";

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

                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
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

    static string ExtractSseText(object node)
    {
        if (node == null) return null;

        string s = node as string;
        if (s != null) return s;

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

            foreach (string key in new[] { "content_list", "content", "text", "message" })
            {
                if (dict.ContainsKey(key))
                {
                    string t = ExtractSseText(dict[key]);
                    if (!string.IsNullOrWhiteSpace(t)) return t;
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
                string t = ExtractSseText(el);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    if (sb.Length > 0) sb.Append("\n");
                    sb.Append(t);
                }
            }

            return sb.ToString();
        }

        return null;
    }
}