// ChatInit.cs — инициализация parent_id цепочки при старте
// New Era v7.2
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
partial class MainConsole
{
static void InitParentIds()
{
if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(ChatId)) {
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

            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            object obj = ser.DeserializeObject(json);
            string leaf = FindDeepestLeaf(obj);
            if (!string.IsNullOrEmpty(leaf)) return leaf;
            return null;
        } catch {
            if (attempt < 2) Thread.Sleep(500 * (attempt + 1));
        }
    }
    return null;
}

static string FindDeepestLeaf(object node)
{
    if (node == null) return null;
    var dict = node as System.Collections.Generic.Dictionary<string, object>;
    if (dict != null) {
        string id = null;
        if (dict.ContainsKey("id")) id = dict["id"] as string;
        object[] children = null;
        if (dict.ContainsKey("childrenIds")) children = dict["childrenIds"] as object[];
        if (children == null || children.Length == 0) return id;
        string lastChild = children[children.Length - 1] as string;
        return lastChild ?? id;
    }
    return null;
}
}