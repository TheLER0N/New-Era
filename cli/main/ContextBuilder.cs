// ContextBuilder.cs — построение контекста проекта с лимитами (fallback без оркестратора)
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

partial class MainConsole
{
    static readonly string[] ContextExtensions = { ".cs", ".bat", ".cmd", ".ps1", ".json", ".xml", ".csproj", ".sln", ".txt", ".cfg", ".ini", ".md" };

    static string BuildContextPayload(string path, int maxTotal, int maxFile) {
        if (string.IsNullOrEmpty(path)) return null;
        var files = new List<string>(); try { if (File.Exists(path)) files.Add(path); else if (Directory.Exists(path)) CollectContextFiles(path, files, 0); } catch { return null; }
        if (files.Count == 0) return null; files.Sort(StringComparer.OrdinalIgnoreCase);
        string baseDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path); if (string.IsNullOrEmpty(baseDir)) baseDir = BaseDir;
        var sb = new StringBuilder(); long total = 0; int included = 0; int skipped = 0;
        foreach (string full in files) {
            string name = Path.GetFileName(full); string rel = MakeRelativePath(baseDir, full);
            if (IsExcludedContextFile(rel, name)) { skipped++; continue; }
            string body; try { body = ReadTextAuto(full); } catch (Exception ex) { body = "// READ ERROR: " + ex.Message; }
            if (body == null) body = ""; body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n');
            bool truncated = false; if (maxFile > 0 && body.Length > maxFile) { body = body.Substring(0, maxFile); truncated = true; }
            long blockLen = (long)body.Length + rel.Length + 40;
            if (maxTotal > 0 && total + blockLen > maxTotal) { skipped++; continue; }
            total += blockLen; included++;
            sb.Append("\n=== FILE: " + rel + " ===\n"); sb.Append(body); sb.Append("\n");
            if (truncated) sb.Append("// [truncated to " + maxFile + " chars]\n"); sb.Append("=== END ===\n");
        }
        if (included == 0) return null;
        if (skipped > 0) sb.Append("\n// [context: skipped " + skipped + " file(s) due to limits/exclusions]\n");
        return sb.ToString();
    }

    static void CollectContextFiles(string path, List<string> files, int depth) {
        var stack = new Stack<string>(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); stack.Push(path);
        while (stack.Count > 0) {
            string current = stack.Pop(); string fullCurrent; try { fullCurrent = Path.GetFullPath(current); } catch { continue; }
            if (!visited.Add(fullCurrent)) continue;
            try {
                string[] fs = Directory.GetFiles(current); Array.Sort(fs, StringComparer.OrdinalIgnoreCase);
                foreach (string f in fs) {
                    string name = Path.GetFileName(f); if (string.IsNullOrEmpty(name)) continue; if (name.StartsWith(".")) continue;
                    string ext = Path.GetExtension(f); if (string.IsNullOrEmpty(ext)) continue; ext = ext.ToLowerInvariant();
                    bool ok = false; foreach (string ce in ContextExtensions) { if (ce == ext) { ok = true; break; } } if (!ok) continue;
                    files.Add(f);
                }
                string[] dirs = Directory.GetDirectories(current); Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs) { string name = Path.GetFileName(d); if (IsExcludedContextDir(name)) continue; stack.Push(d); }
            } catch { }
        }
    }

    static bool IsExcludedContextDir(string name) {
        if (string.IsNullOrEmpty(name)) return true; if (name.StartsWith(".")) return true; string n = name.ToLowerInvariant();
        return n == "bin" || n == "obj" || n == "program_from_the_cli" || n == ".git" || n == ".vs" || n == ".vscode" || n == ".idea" || n == "node_modules";
    }
    static bool IsExcludedContextFile(string rel, string name) {
        if (string.IsNullOrEmpty(name)) return true; string n = name.ToLowerInvariant();
        if (n == "qwen_config.txt") return true; if (n == "chat_history.dat") return true; if (n == "qwen_cursor.txt") return true; if (n == "plan.txt") return true;
        if (n.StartsWith("last_") && n.EndsWith(".json")) return true; if (n.StartsWith("request_") && n.EndsWith(".txt")) return true; if (n.EndsWith("_report.txt")) return true;
        string r = (rel ?? "").Replace('\\', '/');
        if (r.IndexOf("program_from_the_cli/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (r.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (r.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (r.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
}