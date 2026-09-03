using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MainApp;

/// <summary>
/// Хранилище памяти проекта (.leron/memory.json).
/// Thread-safe через lock. Все операции только здесь.
/// </summary>
public static class MemoryStore
{
    private static readonly object _lock = new();
    private static readonly string[] ValidCats = { "choices", "facts", "files", "notes" };
    private const int MAX_TEXT = 300; // LERON UPDATE
    private const int MAX_SHORT = 5; // LERON UPDATE

    public static string MemPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("root пустой");
        // используем существующий EnsureLeronFolder из проекта
        var t = typeof(MemoryStore).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "LeronFiles" || t.Name == "LeronMeta" || t.Name == "FileIndexer" || t.Name.Contains("Leron"));
        // прямой вызов через рефлексию, чтобы не делать жёсткую зависимость
        foreach (var tp in typeof(MemoryStore).Assembly.GetTypes())
        {
            var m = tp.GetMethod("EnsureLeronFolder", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m != null && m.GetParameters().Length == 1) { try { m.Invoke(null, new object?[] { root }); break; } catch { } }
        }
        var dir = Path.Combine(root, ".leron");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "memory.json");
    }

    public static JsonObject Load(string root)
    {
        lock (_lock)
        {
            var path = MemPath(root);
            if (!File.Exists(path)) return Empty();
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return Empty();
                var node = JsonNode.Parse(json);
                var obj = node as JsonObject;
                if (obj == null) return Empty();
                if (!obj.ContainsKey("long_term")) obj["long_term"] = new JsonArray();
                if (!obj.ContainsKey("short_term")) obj["short_term"] = new JsonArray();
                return obj;
            }
            catch { return Empty(); }
        }
    }

    public static void Save(string root, JsonObject mem)
    {
        lock (_lock)
        {
            var path = MemPath(root);
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(path, mem.ToJsonString(opts), Encoding.UTF8);
        }
    }

    public static JsonObject Empty()
    {
        return new JsonObject
        {
            ["version"] = 1,
            ["long_term"] = new JsonArray(),
            ["short_term"] = new JsonArray()
        };
    }

    /// <summary>Поиск по title+text (регистронезависимо).</summary>
    public static List<JsonObject> Search(string root, string query, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<JsonObject>();
        var q = query.Trim().ToLower();
        var mem = Load(root);
        var arr = mem["long_term"] as JsonArray ?? new JsonArray();
        var result = new List<JsonObject>();
        foreach (var n in arr)
        {
            var o = n as JsonObject;
            if (o == null) continue;
            var title = (o["title"]?.ToString() ?? "").ToLower();
            var text = (o["text"]?.ToString() ?? "").ToLower();
            if (title.Contains(q) || text.Contains(q))
            {
                var snippet = Snippet(o["text"]?.ToString() ?? "", 120);
                var copy = new JsonObject
                {
                    ["id"] = o["id"]?.ToString(),
                    ["cat"] = o["cat"]?.ToString(),
                    ["title"] = o["title"]?.ToString(),
                    ["snippet"] = snippet
                };
                result.Add(copy);
                if (result.Count >= limit) break;
            }
        }
        return result;
    }

    /// <summary>Полные карточки по списку id.</summary>
    public static List<JsonObject> Read(string root, IEnumerable<string> ids)
    {
        if (ids == null) return new List<JsonObject>();
        var want = new HashSet<string>(ids.Where(x => !string.IsNullOrEmpty(x)));
        if (want.Count == 0) return new List<JsonObject>();
        var mem = Load(root);
        var arr = mem["long_term"] as JsonArray ?? new JsonArray();
        var result = new List<JsonObject>();
        foreach (var n in arr)
        {
            var o = n as JsonObject;
            if (o == null) continue;
            var id = o["id"]?.ToString();
            if (id != null && want.Contains(id))
            {
                // deepcopy через JSON round-trip
                var copy = JsonNode.Parse(o.ToJsonString()) as JsonObject;
                if (copy != null) result.Add(copy);
            }
        }
        return result;
    }

    /// <summary>Создать или обновить карточку. Возвращает id.</summary>
    public static string Upsert(string root, string? id, string cat, string title, string text, IEnumerable<string>? links)
    {
        if (!ValidCats.Contains(cat)) throw new ArgumentException($"cat '{cat}' недопустим (разрешены: {string.Join(",", ValidCats)})");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("title пустой");
        title = title.Trim();
        text = Truncate((text ?? "").Trim(), MAX_TEXT);

        var now = DateTime.UtcNow.ToString("o");
        var mem = Load(root);
        var arr = mem["long_term"] as JsonArray ?? new JsonArray();

        var linkArr = new JsonArray();
        if (links != null) foreach (var l in links) if (!string.IsNullOrEmpty(l)) linkArr.Add(l);

        // обновление по id
        if (!string.IsNullOrEmpty(id))
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var o = arr[i] as JsonObject;
                if (o == null) continue;
                if (o["id"]?.ToString() == id)
                {
                    arr[i] = new JsonObject
                    {
                        ["id"] = id,
                        ["cat"] = cat,
                        ["title"] = title,
                        ["text"] = text,
                        ["links"] = linkArr,
                        ["created"] = o["created"]?.ToString() ?? now,
                        ["updated"] = now
                    };
                    Save(root, mem);
                    return id;
                }
            }
        }

        // новая карточка
        var newId = NextId(arr);
        arr.Add(new JsonObject
        {
            ["id"] = newId,
            ["cat"] = cat,
            ["title"] = title,
            ["text"] = text,
            ["links"] = linkArr,
            ["created"] = now,
            ["updated"] = now
        });
        Save(root, mem);
        return newId;
    }

    /// <summary>Удалить карточку по id. Возвращает true если удалена.</summary>
    public static bool Forget(string root, string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var mem = Load(root);
        var arr = mem["long_term"] as JsonArray ?? new JsonArray();
        for (int i = 0; i < arr.Count; i++)
        {
            if (arr[i] is JsonObject o && o["id"]?.ToString() == id)
            {
                arr.RemoveAt(i);
                Save(root, mem);
                return true;
            }
        }
        return false;
    }

    /// <summary>Удалить всю память.</summary>
    public static void ForgetAll(string root)
    {
        var mem = Empty();
        Save(root, mem);
    }

    /// <summary>Добавить краткосрочную запись. Держит ровно MAX_SHORT (5).</summary>
    public static void PushShort(string root, string user, string ai)
    {
        var mem = Load(root);
        var arr = mem["short_term"] as JsonArray ?? new JsonArray();
        arr.Add(new JsonObject
        {
            ["user"] = Truncate((user ?? "").Trim(), 150),
            ["ai"] = Truncate((ai ?? "").Trim(), 250),
            ["time"] = DateTime.Now.ToString("HH:mm")
        });
        while (arr.Count > MAX_SHORT) arr.RemoveAt(0);
        Save(root, mem);
    }

    /// <summary>Сводка для промпта: счётчики по категориям.</summary>
    public static (int total, Dictionary<string, int> byCat) Summary(string root)
    {
        var mem = Load(root);
        var arr = mem["long_term"] as JsonArray ?? new JsonArray();
        var byCat = new Dictionary<string, int>();
        foreach (var c in ValidCats) byCat[c] = 0;
        foreach (var n in arr)
        {
            if (n is JsonObject o)
            {
                var c = o["cat"]?.ToString();
                if (c != null && byCat.ContainsKey(c)) byCat[c]++;
            }
        }
        return (arr.Count, byCat);
    }

    /// <summary>Краткосрочные записи для промпта.</summary>
    public static List<JsonObject> ShortTerm(string root)
    {
        var mem = Load(root);
        var arr = mem["short_term"] as JsonArray ?? new JsonArray();
        var res = new List<JsonObject>();
        foreach (var n in arr) if (n is JsonObject o) res.Add(o);
        return res;
    }

    // ── helpers ──
    private static string NextId(JsonArray arr)
    {
        int max = 0;
        foreach (var n in arr)
        {
            if (n is JsonObject o)
            {
                var id = o["id"]?.ToString() ?? "";
                if (id.StartsWith("m") && int.TryParse(id.Substring(1), out var num) && num > max)
                    max = num;
            }
        }
        return "m" + (max + 1).ToString("D3");
    }

    private static string Snippet(string s, int max)
    {
        s = (s ?? "").Trim().Replace("\r", " ").Replace("\n", " ");
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }

    private static string Truncate(string s, int max)
    {
        if (s == null) return "";
        return s.Length <= max ? s : s.Substring(0, max);
    }
}