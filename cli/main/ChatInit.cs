// ChatInit.cs — инициализация parent_id цепочки при старте
// New Era v7.1
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

class NodeInfo
{
    public string ParentId;
    public List<string> Children;
}

partial class MainConsole
{
    static void InitParentIds()
    {
        if (!string.IsNullOrEmpty(ChatId) && !string.IsNullOrEmpty(Token)) {
            try {
                string leaf = FetchChatLeafId(ApiBaseUrl, Token, CookieHeader, ChatId);
                if (!string.IsNullOrEmpty(leaf)) LastResponseId = leaf;
            } catch { }
        }

        if (IsAi2Configured()) {
            try {
                string leaf = FetchChatLeafId(GetAi2Api(), GetAi2Token(), null, ChatId2);
                if (!string.IsNullOrEmpty(leaf)) LastAi2ResponseId = leaf;
            } catch { }
        }
    }

    static string FetchChatLeafId(string apiBase, string token, string cookieHeader, string chatId)
    {
        if (string.IsNullOrEmpty(apiBase) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId))
            return null;

        // P1: retry с backoff для стартовой инициализации
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                string url = apiBase.TrimEnd('/') + "/api/v2/chats/" + Uri.EscapeDataString(chatId);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 10000;
                req.ReadWriteTimeout = 20000;
                req.KeepAlive = false;

                req.Accept = "application/json, text/plain, */*";
                req.Headers["source"] = "web";
                req.Headers["Origin"] = "https://chat.qwen.ai";
                req.Referer = "https://chat.qwen.ai/";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
                req.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;

                string cookieValue = !string.IsNullOrEmpty(cookieHeader) ? cookieHeader : ("token=" + token);
                try {
                    var cc = new CookieContainer();
                    cc.SetCookies(new Uri(apiBase), cookieValue);
                    req.CookieContainer = cc;
                } catch {
                    try { req.Headers[HttpRequestHeader.Cookie] = cookieValue; } catch { }
                }

                string json;
                try {
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream stream = resp.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
                        json = reader.ReadToEnd();
                    }
                } catch {
                    if (attempt < 2) { Thread.Sleep(500 * (attempt + 1)); continue; }
                    return null;
                }

                if (string.IsNullOrEmpty(json) || json.TrimStart().StartsWith("<")) return null;
                return ExtractLeafId(json);
            } catch {
                if (attempt < 2) Thread.Sleep(500 * (attempt + 1));
            }
        }

        return null;
    }

    static string ExtractLeafId(string json)
    {
        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        object root;
        try { root = ser.DeserializeObject(json); } catch { return null; }

        var byId = new Dictionary<string, NodeInfo>();
        var order = new List<string>();
        CollectNodes(root, byId, order);

        if (order.Count == 0) return null;

        var hasParent = new HashSet<string>();
        foreach (var kv in byId)
            if (kv.Value.Children != null)
                foreach (string c in kv.Value.Children)
                    if (byId.ContainsKey(c)) hasParent.Add(c);

        var roots = new List<string>();
        foreach (string id in order) {
            NodeInfo ni = byId[id];
            bool noParent = string.IsNullOrEmpty(ni.ParentId) || !byId.ContainsKey(ni.ParentId);
            if (noParent && !hasParent.Contains(id)) roots.Add(id);
        }

        if (roots.Count == 0) roots.AddRange(order);

        string last = null;
        var visited = new HashSet<string>();
        foreach (string r in roots) DfsLeaf(r, byId, visited, ref last);

        return last;
    }

    static void CollectNodes(object node, Dictionary<string, NodeInfo> byId, List<string> order)
    {
        if (node == null) return;

        var dict = node as Dictionary<string, object>;
        if (dict != null) {
            string role = dict.ContainsKey("role") ? dict["role"] as string : null;

            if (role == "user" || role == "assistant" || role == "model" || role == "system") {
                string id = dict.ContainsKey("id") ? dict["id"] as string : null;

                if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id)) {
                    string pid = dict.ContainsKey("parentId") ? dict["parentId"] as string : null;
                    var kids = new List<string>();

                    if (dict.ContainsKey("childrenIds")) {
                        object[] kidsArr = dict["childrenIds"] as object[];
                        if (kidsArr != null)
                            foreach (object k in kidsArr) {
                                string ks = k as string;
                                if (!string.IsNullOrEmpty(ks)) kids.Add(ks);
                            }
                    }

                    byId[id] = new NodeInfo { ParentId = pid, Children = kids };
                    order.Add(id);
                }
                return;
            }

            foreach (var kv in dict) CollectNodes(kv.Value, byId, order);
            return;
        }

        object[] arr = node as object[];
        if (arr != null)
            for (int i = 0; i < arr.Length; i++) CollectNodes(arr[i], byId, order);
    }

    static void DfsLeaf(string id, Dictionary<string, NodeInfo> byId, HashSet<string> visited, ref string last)
    {
        if (visited.Contains(id)) return;
        visited.Add(id);
        last = id;

        NodeInfo ni;
        if (!byId.TryGetValue(id, out ni)) return;
        if (ni.Children == null) return;

        foreach (string c in ni.Children)
            if (byId.ContainsKey(c)) DfsLeaf(c, byId, visited, ref last);
    }
}