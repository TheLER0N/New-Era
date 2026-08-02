// Program.cs — точка входа, состояния, конфигурация
// New Era CLI v6.0
// C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
partial class MainConsole
{
// ── Константы ──────────────────────────────────────────────
const string PipeName       = "NewEraMainPipe";
const string DefaultApiBase = "https://chat.qwen.ai";
const int MaxHistoryEntries = 200;
static readonly string BaseDir     = AppDomain.CurrentDomain.BaseDirectory;
static readonly string ConfigFile  = Path.Combine(BaseDir, "qwen_config.txt");
static readonly string HistoryFile = Path.Combine(BaseDir, "chat_history.dat");
static readonly string DumpFile    = Path.Combine(BaseDir, "last_sse.json");
static readonly string VersionFile = Path.Combine(BaseDir, "version.txt");
// ── Состояние ──────────────────────────────────────────────
static string Token        = null;
static string ChatId       = null;
static string ApiBaseUrl   = DefaultApiBase;
static string CookieHeader = null;
static string AppVersion   = "6.0";
static readonly object PrintLock   = new object();
static readonly object HistoryLock = new object();
static readonly CancellationTokenSource Cts = new CancellationTokenSource();
static Process liveHelper = null;
static volatile bool StopRequested = false;
static List<HistoryEntry> History  = new List<HistoryEntry>();
// parent_id цепочки: чтобы каждое обращение ПИСАЛО НОВОЕ сообщение
// в конец чата, а не создавало новый корень (= «менял первое сообщение»).
static volatile string LastResponseId    = null; // Primary чат
static volatile string LastAi2ResponseId = null; // AI #2 чат
static volatile bool SpinnerActive = false;
static Thread SpinnerThread        = null;
// Настройки анимаций
static bool AnimationsEnabled  = true;
static int  TypewriterDelayMs  = 3;
static bool ShowThinking       = false;
// ══════════════════════════════════════════════════════════
//  ENTRY
// ══════════════════════════════════════════════════════════
static int Main(string[] args)
{
try { Console.OutputEncoding = Encoding.UTF8; } catch { }
try { Console.InputEncoding  = Encoding.UTF8; } catch { }
Console.Title = "New Era v6";
try { Console.CursorVisible = true; } catch { }
try
{
ServicePointManager.SecurityProtocol =
(SecurityProtocolType)3072 | (SecurityProtocolType)768;
}
catch { }
Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e)
{
StopRequested = true;
e.Cancel = false;
};
try { LoadAppVersion(); } catch { }
try { LoadConfig(); }     catch { }
try { LoadHistory(); }    catch { }
// Подтягиваем текущий «лист» каждого чата, чтобы первое же
// обращение добавляло новое сообщение, а не трогало корень.
// Любая ошибка некритична: тогда parent_id останется null.
try { InitParentIds(); } catch { }
try { Console.Clear(); }  catch { }
DrawBanner();
Thread listener = new Thread(() => PipeListener(Cts.Token));
listener.IsBackground = true;
listener.Start();
int exitCode = 0;
try
{
Repl();
}
catch (Exception ex)
{
WriteColored(ConsoleColor.Red, "  ✖ Критическая ошибка: " + ex.Message + "\n");
exitCode = 1;
}
finally
{
StopLive();
try { SaveHistory(); } catch { }
try { Cts.Cancel(); }  catch { }
try { listener.Join(1500); } catch { }
}
return exitCode;
}
static void LoadAppVersion()
{
try
{
if (File.Exists(VersionFile))
{
string v = ReadTextAuto(VersionFile).Trim();
if (v.Length > 0 && v.Length <= 20)
AppVersion = v;
}
}
catch
{
}
}
}