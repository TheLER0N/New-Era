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
    static void SetPrimaryParent(string id)
    {
        if (!string.IsNullOrEmpty(id))
            LastResponseId = id;
    }

    static void SetAi2Parent(string id)
    {
        if (!string.IsNullOrEmpty(id))
            LastAi2ResponseId = id;
    }

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
                object obj = ser.DeserializeObject(trimmed);

                string phase = GetJsonPhase(obj);
                if (!IsThinkingPhase(phase))
                {
                    string t = ExtractBestText(obj);
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

        string glued = RegexGlueAll(raw);
        if (!string.IsNullOrEmpty(glued))
            return glued;

        if (!trimmed.StartsWith("{") && !trimmed.StartsWith("<"))
            return trimmed.Trim();

        return null;
    }

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

    static string ParseSseAnswerEx(string raw, Action<string> parentSetter)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        var answer = new StringBuilder();
        var summary = new StringBuilder();
        var fallback = new StringBuilder();

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

            Match rid = Regex.Match(data, "\"response_id\"\\s*:\\s*\"([^\"]+)\"");
            if (rid.Success)
                newResponseId = rid.Groups[1].Value;

            bool isCompleted =
                data.Contains("response.completed") ||
                data.Contains("\"status\":\"completed\"") ||
                data.Contains("\"status\": \"completed\"") ||
                data.Contains("\"status\":\"finished\"") ||
                data.Contains("\"status\": \"finished\"");

            object obj = null;

            try
            {
                obj = ser.DeserializeObject(data);
            }
            catch
            {
            }

            if (obj != null)
            {
                string rid2 = FindJsonString(obj, "response_id", 0);
                if (!string.IsNullOrEmpty(rid2))
                    newResponseId = rid2;

                string phase = GetJsonPhase(obj);

                if (!IsThinkingPhase(phase))
                {
                    StringBuilder target = IsSummaryPhase(phase) ? summary : answer;

                    if (isCompleted)
                    {
                        string completed = ExtractCompletedText(obj);
                        if (!string.IsNullOrEmpty(completed))
                        {
                            target.Length = 0;
                            target.Append(completed);
                            continue;
                        }
                    }

                    string piece = ExtractBestText(obj);
                    if (!string.IsNullOrEmpty(piece))
                        AppendStreamPiece(target, piece, isCompleted);
                }
            }
            else
            {
                if (!Regex.IsMatch(
                    data,
                    "\"phase\"\\s*:\\s*\"[^\"]*(think|thinking|reason|reflection)[^\"]*\""))
                {
                    string piece = ExtractLongestRegexPiece(data);
                    if (!string.IsNullOrEmpty(piece))
                        AppendStreamPiece(fallback, piece, isCompleted);
                }
            }
        }

        if (!string.IsNullOrEmpty(newResponseId) && parentSetter != null)
            parentSetter(newResponseId);

        string result = ChooseResult(answer, summary, fallback);

        if (string.IsNullOrEmpty(result))
        {
            string trimmed = raw.TrimStart();

            if (trimmed.StartsWith("{"))
            {
                try
                {
                    var ser2 = new JavaScriptSerializer();
                    ser2.MaxJsonLength = int.MaxValue;
                    object obj = ser2.DeserializeObject(trimmed);

                    string phase = GetJsonPhase(obj);
                    if (!IsThinkingPhase(phase))
                    {
                        string t = ExtractBestText(obj);
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

        if (string.IsNullOrEmpty(result))
        {
            string trimmed = raw.Trim();

            if (trimmed.Length > 0 &&
                !trimmed.StartsWith("{") &&
                !trimmed.StartsWith("<") &&
                !trimmed.Contains("data:"))
            {
                result = trimmed;
            }
        }

        return result;
    }

    static string ChooseResult(StringBuilder answer, StringBuilder summary, StringBuilder fallback)
    {
        string a = answer.ToString().Trim();
        if (a.Length > 0)
            return a;

        string s = summary.ToString().Trim();
        if (s.Length > 0)
            return s;

        return fallback.ToString().Trim();
    }

    static void AppendStreamPiece(StringBuilder sb, string piece, bool isCompleted)
    {
        if (string.IsNullOrEmpty(piece))
            return;

        string current = sb.ToString();

        if (current.Length == 0)
        {
            sb.Append(piece);
            return;
        }

        if (string.Equals(current, piece, StringComparison.Ordinal))
            return;

        string currentNoWs = NormalizeNoSpaces(current);
        string pieceNoWs = NormalizeNoSpaces(piece);

        if (isCompleted)
        {
            if (pieceNoWs.Length == 0)
                return;

            if (currentNoWs.Length == 0)
            {
                sb.Length = 0;
                sb.Append(piece);
                return;
            }

            if (pieceNoWs.IndexOf(currentNoWs, StringComparison.Ordinal) >= 0)
            {
                sb.Length = 0;
                sb.Append(piece);
                return;
            }

            if (currentNoWs.IndexOf(pieceNoWs, StringComparison.Ordinal) >= 0)
            {
                if (piece.IndexOf(' ') >= 0 ||
                    pieceNoWs.Length >= 12 ||
                    pieceNoWs.Length >= currentNoWs.Length / 3)
                {
                    sb.Length = 0;
                    sb.Append(piece);
                    return;
                }

                return;
            }

            if (piece.Length >= current.Length)
            {
                sb.Length = 0;
                sb.Append(piece);
                return;
            }

            sb.Append(piece);
            return;
        }

        if (currentNoWs.Length > 0 &&
            pieceNoWs.Length > 0 &&
            pieceNoWs.IndexOf(currentNoWs, StringComparison.Ordinal) >= 0 &&
            piece.Length > current.Length)
        {
            sb.Length = 0;
            sb.Append(piece);
            return;
        }

        sb.Append(piece);
    }

    static string NormalizeNoSpaces(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";

        var sb = new StringBuilder();

        foreach (char c in s)
        {
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    static string GetJsonPhase(object node)
    {
        return FindJsonString(node, "phase", 0);
    }

    static string FindJsonString(object node, string key, int depth)
    {
        if (node == null || depth > 12)
            return null;

        Dictionary<string, object> dict = node as Dictionary<string, object>;

        if (dict != null)
        {
            if (dict.ContainsKey(key))
            {
                string s = dict[key] as string;
                if (!string.IsNullOrEmpty(s))
                    return s;

                string nested = FindJsonString(dict[key], key, depth + 1);
                if (!string.IsNullOrEmpty(nested))
                    return nested;
            }

            foreach (var kv in dict)
            {
                string s = FindJsonString(kv.Value, key, depth + 1);
                if (!string.IsNullOrEmpty(s))
                    return s;
            }

            return null;
        }

        object[] arr = node as object[];

        if (arr != null)
        {
            foreach (object el in arr)
            {
                string s = FindJsonString(el, key, depth + 1);
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }

        return null;
    }

    static bool IsThinkingPhase(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return false;

        string p = phase.ToLowerInvariant();

        return p.Contains("think") ||
               p.Contains("reason") ||
               p.Contains("reflection") ||
               p.Contains("thought");
    }

    static bool IsSummaryPhase(string phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return false;

        return phase.ToLowerInvariant().Contains("summary");
    }

    static string ExtractBestText(object node)
    {
        var candidates = new List<string>();
        CollectBestCandidates(node, candidates, 0);
        return ChooseLongestCandidate(candidates);
    }

    static void CollectBestCandidates(object node, List<string> candidates, int depth)
    {
        if (node == null || depth > 16)
            return;

        string s = node as string;
        if (s != null)
        {
            candidates.Add(s);
            return;
        }

        Dictionary<string, object> dict = node as Dictionary<string, object>;

        if (dict != null)
        {
            if (dict.ContainsKey("phase"))
            {
                string ph = dict["phase"] as string;
                if (IsThinkingPhase(ph))
                    return;
            }

            string[] keys =
            {
                "content",
                "text",
                "message",
                "delta",
                "choices",
                "content_list",
                "output",
                "result",
                "response",
                "answer",
                "body",
                "data"
            };

            foreach (string key in keys)
            {
                if (!dict.ContainsKey(key))
                    continue;

                object val = dict[key];

                if (key == "choices")
                {
                    string t = ExtractBestFromChoices(val);
                    if (!string.IsNullOrEmpty(t))
                        candidates.Add(t);
                }
                else if (key == "content_list")
                {
                    string t = ExtractConcatArray(val);
                    if (!string.IsNullOrEmpty(t))
                        candidates.Add(t);
                }
                else
                {
                    CollectBestCandidates(val, candidates, depth + 1);
                }
            }

            return;
        }

        object[] arr = node as object[];

        if (arr != null)
        {
            var parts = new List<string>();

            foreach (object el in arr)
            {
                string t = ExtractBestText(el);
                if (!string.IsNullOrEmpty(t))
                    parts.Add(t);
            }

            string longest = ChooseLongestCandidate(parts);
            if (!string.IsNullOrEmpty(longest))
                candidates.Add(longest);

            return;
        }
    }

    static string ExtractBestFromChoices(object node)
    {
        if (node == null)
            return null;

        object[] arr = node as object[];

        if (arr != null)
        {
            var parts = new List<string>();

            foreach (object el in arr)
            {
                string t = ExtractBestText(el);
                if (!string.IsNullOrEmpty(t))
                    parts.Add(t);
            }

            return ChooseLongestCandidate(parts);
        }

        return ExtractBestText(node);
    }

    static string ExtractConcatArray(object node)
    {
        if (node == null)
            return null;

        object[] arr = node as object[];

        if (arr != null)
        {
            var sb = new StringBuilder();

            foreach (object el in arr)
            {
                string t = ExtractBestText(el);
                if (!string.IsNullOrEmpty(t))
                    sb.Append(t);
            }

            return sb.ToString();
        }

        return ExtractBestText(node);
    }

    static string ExtractCompletedText(object node)
    {
        var candidates = new List<string>();
        CollectCompletedCandidates(node, candidates, 0);
        return ChooseLongestCandidate(candidates);
    }

    static void CollectCompletedCandidates(object node, List<string> candidates, int depth)
    {
        if (node == null || depth > 16)
            return;

        string s = node as string;
        if (s != null)
        {
            candidates.Add(s);
            return;
        }

        Dictionary<string, object> dict = node as Dictionary<string, object>;

        if (dict != null)
        {
            if (dict.ContainsKey("phase"))
            {
                string ph = dict["phase"] as string;
                if (IsThinkingPhase(ph))
                    return;
            }

            string[] keys =
            {
                "message",
                "content",
                "text",
                "output",
                "result",
                "response",
                "answer",
                "body",
                "data",
                "choices"
            };

            foreach (string key in keys)
            {
                if (!dict.ContainsKey(key))
                    continue;

                object val = dict[key];

                if (key == "choices")
                {
                    string t = ExtractCompletedFromChoices(val);
                    if (!string.IsNullOrEmpty(t))
                        candidates.Add(t);
                }
                else
                {
                    CollectCompletedCandidates(val, candidates, depth + 1);
                }
            }

            return;
        }

        object[] arr = node as object[];

        if (arr != null)
        {
            var parts = new List<string>();

            foreach (object el in arr)
            {
                string t = ExtractCompletedText(el);
                if (!string.IsNullOrEmpty(t))
                    parts.Add(t);
            }

            string longest = ChooseLongestCandidate(parts);
            if (!string.IsNullOrEmpty(longest))
                candidates.Add(longest);

            return;
        }
    }

    static string ExtractCompletedFromChoices(object node)
    {
        if (node == null)
            return null;

        object[] arr = node as object[];

        if (arr != null)
        {
            var parts = new List<string>();

            foreach (object el in arr)
            {
                string t = ExtractCompletedText(el);
                if (!string.IsNullOrEmpty(t))
                    parts.Add(t);
            }

            return ChooseLongestCandidate(parts);
        }

        return ExtractCompletedText(node);
    }

    static string ChooseLongestCandidate(List<string> candidates)
    {
        string best = null;
        int bestLen = -1;

        foreach (string c in candidates)
        {
            if (c == null)
                continue;

            if (c.Length > bestLen)
            {
                best = c;
                bestLen = c.Length;
            }
        }

        return best;
    }

    static string ExtractLongestRegexPiece(string data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        string best = null;
        int bestLen = -1;

        foreach (Match cm in Regex.Matches(
            data,
            "\"(?:content|text|delta|answer|result|output)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\""))
        {
            string part = UnescapeJson(cm.Groups[1].Value);

            if (part != null && part.Length > bestLen)
            {
                best = part;
                bestLen = part.Length;
            }
        }

        return best;
    }

    static string RegexGlueAll(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        var sb = new StringBuilder();

        string[] lines = raw.Split(new[] { "\n" }, StringSplitOptions.None);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (Regex.IsMatch(
                line,
                "\"phase\"\\s*:\\s*\"[^\"]*(think|thinking|reason|reflection)[^\"]*\""))
            {
                continue;
            }

            string piece = ExtractLongestRegexPiece(line);
            if (!string.IsNullOrEmpty(piece))
                AppendStreamPiece(sb, piece, false);
        }

        if (sb.Length > 0)
            return sb.ToString().Trim();

        string single = ExtractLongestRegexPiece(raw);
        if (!string.IsNullOrEmpty(single))
            return single.Trim();

        return null;
    }

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
                    case '"':
                        sb.Append('"');
                        i++;
                        break;

                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;

                    case '/':
                        sb.Append('/');
                        i++;
                        break;

                    case 'n':
                        sb.Append('\n');
                        i++;
                        break;

                    case 'r':
                        sb.Append('\r');
                        i++;
                        break;

                    case 't':
                        sb.Append('\t');
                        i++;
                        break;

                    case 'b':
                        sb.Append('\b');
                        i++;
                        break;

                    case 'f':
                        sb.Append('\f');
                        i++;
                        break;

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

    static string ExtractSseText(object node)
    {
        return ExtractBestText(node);
    }

    static string ExtractSseTextCore(object node, int depth)
    {
        return ExtractBestText(node);
    }
}