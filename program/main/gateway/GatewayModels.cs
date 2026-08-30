using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace MainApp;
internal static class JsonOpts
{
public static readonly JsonSerializerOptions Ci = new() { PropertyNameCaseInsensitive = true };
}
class Config
{
public Dictionary<string, RoleConfig> Roles { get; set; } = new();
public int RetryAttempts { get; set; } = 3;
public List<ProjectConfig> Projects { get; set; } = new();
public List<string> AutoApprove { get; set; } = new();
public Dictionary<string, ProjectSettings> ProjectSettings { get; set; } = new();
public HubSettingsDto HubSettings { get; set; } = new();
}
class ProjectConfig
{
public string Name { get; set; } = "";
public string Path { get; set; } = "";
public string Role { get; set; } = "";
public DateTime? LastOpened { get; set; }
}
class RoleConfig
{
public string ChatId { get; set; } = "";
public string Url { get; set; } = "";
}
class ProjectSettings
{
public string? CheckCommand { get; set; }
public int? MaxSteps { get; set; }
public bool? AutoRepair { get; set; }
}
class SendRequest
{
public string Role { get; set; } = "";
public string Text { get; set; } = "";
public bool Think { get; set; }
}
class AgentRequest
{
public string Role { get; set; } = "";
public string Text { get; set; } = "";
public string ProjectPath { get; set; } = "";
public string Mode { get; set; } = "edit";
public bool Think { get; set; }
public bool AutoRepair { get; set; } = true;
public int PlanRounds { get; set; } = 1;
public int PlanMin { get; set; } = 1;
public int PlanMax { get; set; } = 3;
}
class ApproveRequest
{
public string SessionId { get; set; } = "";
public bool Approve { get; set; }
public bool Remember { get; set; }
public int Steps { get; set; }
public string? InputText { get; set; }
}
class CancelRequest
{
public string Role { get; set; } = "";
}
class QuestionDto
{
public string Id { get; set; } = "";
public string Text { get; set; } = "";
public List<string> Options { get; set; } = new();
public bool AllowCustom { get; set; } = true;
}
class FileIndexEntry
{
public string Summary { get; set; } = "";
public DateTime? MTime { get; set; }
public long Size { get; set; }
}
class FileIndex
{
public Dictionary<string, FileIndexEntry> Files { get; set; } = new();
}
class AgentSession
{
public string Role { get; set; } = "";
public string? Root { get; set; }
public string Mode { get; set; } = "edit";
public bool Think { get; set; }
public bool AllowTools { get; set; }
public string BrowserNextPrompt { get; set; } = "";
public JsonArray Messages { get; set; } = new();
public List<string> ToolLog { get; set; } = new();
public Queue<PendingTool> Pending { get; set; } = new();
public List<ActionCard> Cards { get; set; } = new();
public int CardsSent { get; set; }
public HashSet<string> ChangedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
public int StepLimit { get; set; } = 30;
public int StepUsed { get; set; }
public int TextRetries { get; set; }
public bool RepairMode { get; set; }
public int RepairAttempts { get; set; }
public bool AutoRepair { get; set; } = true;
public int PlanRounds { get; set; } = 1;
public int PlanMin { get; set; } = 1;
public int PlanMax { get; set; } = 3;
public List<OutsideGrant> OutsideGrants { get; set; } = new();
public HashSet<string> DangerApproved { get; set; } = new(StringComparer.OrdinalIgnoreCase);
public Dictionary<string, (DateTime MTime, long Length)> ReadCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
public HashSet<string> SelfModified { get; set; } = new(StringComparer.OrdinalIgnoreCase);
public bool HasContext { get; set; }
public string LastCheckError { get; set; } = "";
public int SameErrorStreak { get; set; }
}
class PendingTool
{
public string Id { get; set; } = "";
public string Name { get; set; } = "";
public JsonObject Args { get; set; } = new();
}
class OutsideGrant
{
public string Path { get; set; } = "";
public HashSet<string> Actions { get; set; } = new();
}
class ActionCard
{
public string Type { get; set; } = "info";
public string Icon { get; set; } = "";
public string Title { get; set; } = "";
public string Status { get; set; } = "";
public string Details { get; set; } = "";
public string? Path { get; set; }
public string? Command { get; set; }
public string? Shell { get; set; }
public int? ExitCode { get; set; }
public int? Count { get; set; }
public bool Backup { get; set; }
public string? OldText { get; set; }
public string? NewText { get; set; }
}
class ToolExecution
{
public string Tool { get; set; } = "";
public string Output { get; set; } = "";
public string Log { get; set; } = "";
public bool Mutated { get; set; }
public string? Path { get; set; }
public ActionCard Card { get; set; } = new();
}
class CommandResult
{
public int ExitCode { get; set; }
public string StdOut { get; set; } = "";
public string StdErr { get; set; } = "";
public string Output { get; set; } = "";
public string Shell { get; set; } = "CMD";
public bool TimedOut { get; set; }
}
internal sealed partial class GatewayState
{
public static string NormPath(string path) => path.TrimEnd('\\', '/');
public static string Truncate(string s, int n) =>
string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
public static string Tail(string s, int n) =>
string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(s.Length - n);
public static string NormCommand(string command) =>
Regex.Replace(command.Trim().ToLowerInvariant(), @"\s+", " ");
public static string CommandKey(string command) => $"run_command:{NormCommand(command)}";
public static bool SkipDir(string name) =>
name is "bin" or "obj" or ".git" or ".vs" or ".vscode" or ".idea" or ".leron" or "node_modules";
public static bool IsBinaryExt(string path)
{
var ext = Path.GetExtension(path).ToLowerInvariant();
return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".pdf"
or ".zip" or ".rar" or ".7z" or ".exe" or ".dll" or ".pdb" or ".bin"
or ".mp3" or ".mp4" or ".avi" or ".mov" or ".woff" or ".woff2" or ".ttf";
}
public static bool ModeAllowsEdit(string mode) =>
mode is "edit" or "auto" or "yolo" or "repair";
public static bool IsMutating(string name) =>
name is "write_file" or "patch_file" or "edit_file" or "rename_file"
or "delete_file" or "create_directory" or "write_files"
or "file_write_full" or "file_write_lines" or "file_insert" or "file_append";
public static bool IsSpecial(string name) =>
name is "request_user_input" or "request_more_steps" or "request_outside_access";
public static bool IsKnownTool(string name) =>
name is "read_file" or "read_files" or "list_files" or "grep"
or "write_file" or "write_files" or "patch_file"
or "edit_file" or "rename_file" or "delete_file" or "create_directory"
or "run_command" or "update_file_summaries"
or "file_read_exact" or "file_write_full" or "file_write_lines"
or "file_insert" or "file_append"
or "request_user_input" or "request_more_steps"
or "request_outside_access" or "finish";
public static bool IsDangerousCommand(string command)
{
var cmd = NormCommand(command);
string[] dangerous =
{
"rm -rf", "rmdir /s", "rd /s", "del /s", "erase /s",
"git push --force", "git push -f", "git reset --hard",
"format", "diskpart", "shutdown", "drop database", "drop table"
};
foreach (var d in dangerous)
if (cmd.Contains(d)) return true;
return false;
}
public static bool IsTestCommand(string command)
{
var cmd = NormCommand(command);
string[] tests =
{
"npm test", "npx jest", "npx vitest", "yarn test", "pnpm test",
"pytest", "dotnet test", "mvn test", "gradle test", "ctest",
"cargo test", "go test", "jest", "vitest"
};
foreach (var t in tests)
if (cmd.Contains(t)) return true;
return false;
}
private static readonly Regex ReadShellRegex = new(
@"(^|[\s|&])(type|cat|more|head|tail|dir|ls|tree|findstr)([\s|&]|$)|get-content|select-object|readalltext",
RegexOptions.IgnoreCase | RegexOptions.Compiled);
public static bool IsReadShellCommand(string command) => false;
public static bool IsDangerousTool(AgentSession s, PendingTool c)
{
if (c.Name != "run_command") return false;
return IsDangerousCommand(GetStr(c.Args, "command"));
}
public static string PathRule(string tool, string fullPath, string? root)
{
try
{
if (root != null)
{
var rootFull = Path.GetFullPath(root);
var full = Path.GetFullPath(fullPath);
if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
{
var rel = Path.GetRelativePath(rootFull, full).Replace('\\', '/');
string dir = Directory.Exists(full)
? rel
: Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
dir = dir.Trim('/');
return $"{tool}:{(string.IsNullOrEmpty(dir) ? "." : dir)}";
}
}
}
catch { }
return $"{tool}:{fullPath.Replace('\\', '/').Trim('/')}";
}
public static string NormEcho(string s) =>
string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\s+", " ").Trim();
public static bool IsEcho(string sent, string text)
{
if (string.IsNullOrEmpty(sent) || string.IsNullOrEmpty(text)) return false;
if (sent == text) return true;
var a = NormEcho(sent);
var b = NormEcho(text);
if (a.Length < 200 || b.Length < 80) return false;
int n = Math.Min(160, Math.Min(a.Length, b.Length));
return b.StartsWith(a.Substring(0, n), StringComparison.OrdinalIgnoreCase);
}
public static string StripProviderMetadata(string text)
{
if (string.IsNullOrEmpty(text)) return text;
var idx = text.LastIndexOf("<details>", StringComparison.OrdinalIgnoreCase);
if (idx >= 0)
{
var end = text.IndexOf("</details>", idx, StringComparison.OrdinalIgnoreCase);
if (end >= 0)
text = text.Substring(0, idx) + text.Substring(end + "</details>".Length);
}
return text.Trim();
}
public static List<(string name, JsonObject args)> TryParseAllToolCalls(string text)
{
var result = new List<(string, JsonObject)>();
if (string.IsNullOrWhiteSpace(text)) return result;
foreach (var candidate in ExtractJsonObjects(text))
{
try
{
var node = ParseToolJson(candidate);
if (node == null) continue;
var name = GetStr(node, "name");
if (string.IsNullOrEmpty(name)) continue;
var args = node["arguments"] as JsonObject;
if (args == null)
{
string? argsStr = null;
try { argsStr = node["arguments"]?.GetValue<string>(); } catch { }
if (!string.IsNullOrWhiteSpace(argsStr))
{
try { args = JsonNode.Parse(argsStr) as JsonObject; } catch { }
}
}
if (!IsKnownTool(name)) continue;
result.Add((name, args ?? new JsonObject()));
if (name == "finish") break;
}
catch { }
}
return result;
}
public static (string name, JsonObject args, bool known)? TryParseAnyToolCall(string text)
{
if (string.IsNullOrWhiteSpace(text)) return null;
(string, JsonObject, bool)? firstUnknown = null;
foreach (var candidate in ExtractJsonObjects(text))
{
try
{
var node = ParseToolJson(candidate);
if (node == null) continue;
var name = GetStr(node, "name");
if (string.IsNullOrEmpty(name)) continue;
var args = node["arguments"] as JsonObject;
if (args == null)
{
string? argsStr = null;
try { argsStr = node["arguments"]?.GetValue<string>(); } catch { }
if (!string.IsNullOrWhiteSpace(argsStr))
{
try { args = JsonNode.Parse(argsStr) as JsonObject; } catch { }
}
}
if (IsKnownTool(name)) return (name, args ?? new JsonObject(), true);
firstUnknown ??= (name, args ?? new JsonObject(), false);
}
catch { }
}
return firstUnknown;
}
public static string RepairJson(string s)
{
static bool IsHex4(string text, int start)
{
for (int k = 0; k < 4; k++)
{
char c = text[start + k];
bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
if (!ok) return false;
}
return true;
}
var sb = new StringBuilder(s.Length + 16);
bool inString = false;
for (int i = 0; i < s.Length; i++)
{
char ch = s[i];
if (!inString)
{
if (ch == '"') inString = true;
sb.Append(ch);
continue;
}
if (ch == '\\')
{
if (i + 1 >= s.Length)
{
sb.Append(@"\\");
continue;
}
char next = s[i + 1];
bool valid = next == '"' || next == '\\' || next == '/' || next == 'b' || next == 'f' || next == 'n' || next == 'r' || next == 't';
if (next == 'u')
valid = i + 5 < s.Length && IsHex4(s, i + 2);
if (valid)
{
sb.Append(ch);
sb.Append(next);
i++;
continue;
}
sb.Append(@"\\");
if (next == '\n') sb.Append(@"n");
else if (next == '\r') sb.Append(@"r");
else if (next == '\t') sb.Append(@"t");
else if (next < ' ') sb.Append(@"u").Append(((int)next).ToString("x4"));
else sb.Append(next);
i++;
continue;
}
if (ch == '"')
{
inString = false;
sb.Append(ch);
continue;
}
switch (ch)
{
case '\n': sb.Append(@"\n"); break;
case '\r': sb.Append(@"\r"); break;
case '\t': sb.Append(@"\t"); break;
default:
if (ch < ' ') sb.Append(@"\u").Append(((int)ch).ToString("x4"));
else sb.Append(ch);
break;
}
}
return sb.ToString();
}
private static JsonObject? ParseToolJson(string candidate)
{
try { return JsonNode.Parse(candidate) as JsonObject; }
catch
{
try { return JsonNode.Parse(RepairJson(candidate)) as JsonObject; }
catch { return null; }
}
}
private static IEnumerable<string> ExtractJsonObjects(string text)
{
for (int i = 0; i < text.Length; i++)
{
if (text[i] != '{') continue;
int end = FindJsonObjectEnd(text, i);
if (end < 0) continue;
yield return text.Substring(i, end - i + 1);
i = end;
}
}
private static int FindJsonObjectEnd(string text, int start)
{
int depth = 0;
bool inString = false;
bool escaped = false;
for (int i = start; i < text.Length; i++)
{
char ch = text[i];
if (inString)
{
if (escaped) escaped = false;
else if (ch == '\\') escaped = true;
else if (ch == '"') inString = false;
continue;
}
if (ch == '"') inString = true;
else if (ch == '{') depth++;
else if (ch == '}')
{
depth--;
if (depth == 0) return i;
}
}
return -1;
}
public static string GetStr(JsonObject args, string key, string def = "")
{
if (args[key] is JsonNode node)
{
try
{
if (node.GetValueKind() == JsonValueKind.String)
return node.GetValue<string>() ?? def;
return node.ToJsonString();
}
catch { return def; }
}
return def;
}
public static int GetInt(JsonObject args, string key, int def)
{
if (args[key] is JsonNode node)
{
try
{
if (node.GetValueKind() == JsonValueKind.Number)
return node.GetValue<int>();
if (int.TryParse(node.ToString(), out var parsed))
return parsed;
}
catch { }
}
return def;
}
public static bool GetBool(JsonObject args, string key, bool def)
{
if (args[key] is JsonNode node)
{
try
{
var kind = node.GetValueKind();
if (kind == JsonValueKind.True) return true;
if (kind == JsonValueKind.False) return false;
if (bool.TryParse(node.ToString(), out var parsed)) return parsed;
}
catch { }
}
return def;
}
public static ActionCard ErrorCard(string title, string status, string pathOrCommand)
{
return new ActionCard
{
Type = "error",
Icon = "⚠️",
Title = title,
Status = status,
Path = pathOrCommand,
Details = status
};
}
public static void CopyDirectory(string source, string dest)
{
Directory.CreateDirectory(dest);
foreach (var file in Directory.GetFiles(source))
File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
foreach (var dir in Directory.GetDirectories(source))
CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
}
}
public class HubSettingsDto {
public string Username { get; set; } = "";
public string Description { get; set; } = "";
}