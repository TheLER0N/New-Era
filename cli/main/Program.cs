// Program.cs — точка входа, состояние, REPL, watchdog helper.exe
// New Era v7.2 · C# 5 / .NET 4.x
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
partial class MainConsole
{
const string PipeName = "NewEraMainPipe";
const string DefaultApiBase = "https://chat.qwen.ai";
// P3: лимиты вынесены в конфиг. ВАЖНО: static int, НЕ const!
static int MaxHistoryEntries = 200;
static int MaxContextTotal   = 120000;
static int MaxContextFile    = 40000;
static int PlanMaxRetries    = 10;
static int PlanRetryDelayMs  = 3000;

static readonly string BaseDir     = AppDomain.CurrentDomain.BaseDirectory;
static readonly string ConfigFile  = Path.Combine(BaseDir, "qwen_config.txt");
static readonly string HistoryFile = Path.Combine(BaseDir, "chat_history.dat");
static readonly string DumpFile    = Path.Combine(BaseDir, "last_sse.json");
static readonly string VersionFile = Path.Combine(BaseDir, "version.txt");

// ── Primary ──
static string Token        = null;
static string ChatId       = null;
static string ApiBaseUrl   = DefaultApiBase;
static string CookieHeader = null;
static string PrimaryModel = "qwen3.8-max-preview";
static string QwenVersion  = "0.2.66";
static string AppVersion   = "7.2";

// ── AI #2 ──
static string Token2      = null;
static string ApiBaseUrl2 = DefaultApiBase;
static string ChatId2     = null;
static string Ai2Model    = null;
const string DefaultAi2Model = "qwen3.7-max";

// ── Флаги v7.x ──
static bool DispatcherEnabled  = true;
static bool CompressEnabled    = true;
static bool ExtractEnabled     = true;
static bool Ai2ValidateEnabled = false;
static bool ArcMode            = true;
static string ProjectPath      = null;

// ── Состояние ──
static readonly object PrintLock   = new object();
static readonly object HistoryLock = new object();
static readonly CancellationTokenSource Cts = new CancellationTokenSource();
static Process liveHelper = null;
static volatile bool StopRequested = false;

// P0: watchdog для helper.exe + поля, которые нужны Pipe.cs
static volatile bool WatchdogEnabled = false;
static volatile bool LiveRequested   = false;
static string LastLiveArgs           = null;
static Thread HelperWatchdogThread   = null;
static readonly object HelperLock    = new object();

static List<HistoryEntry> History  = new List<HistoryEntry>();
static volatile string LastResponseId    = null;
static volatile string LastAi2ResponseId = null;
static volatile bool SpinnerActive = false;
static Thread SpinnerThread        = null;
static bool AnimationsEnabled = true;
static bool ShowThinking      = false;

// P1: Rollback & ChangeLog
const int MaxChangeLogEntries = 100;
const int MaxRollbackEntries = 20;
static readonly List<string> ChangeLog = new List<string>();
static readonly List<RollbackEntry> RollbackHistory = new List<RollbackEntry>();

static int Main(string[] args)
{
    try { Console.OutputEncoding = Encoding.UTF8; } catch { }
    Console.Title = "New Era v7";

    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768; } catch { }

    Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e) {
        StopRequested = true;
        e.Cancel = false;
    };

    try { LoadAppVersion(); } catch { }

    try {
        if (!File.Exists(ConfigFile)) {
            WriteColored(ConsoleColor.Yellow, "  \u26A0 qwen_config.txt не найден. Создан шаблон.\n");
            TryCreateDefaultConfig();
        }
        LoadConfig();
    } catch { }

    try { LoadHistory(); }    catch { }
    try { InitParentIds(); }  catch { }
    try { Console.Clear(); }  catch { }

    DrawBanner();

    Thread listener = new Thread(delegate() { PipeListener(Cts.Token); });
    listener.IsBackground = true;
    listener.Start();

    StartHelperWatchdog();

    int exitCode = 0;
    try { Repl(); }
    catch (Exception ex) {
        WriteColored(ConsoleColor.Red, "  \u2716 Критическая ошибка: " + ex.Message + "\n");
        exitCode = 1;
    }
    finally {
        WatchdogEnabled = false;
        StopLive();
        try { SaveHistory(); } catch { }
        try { Cts.Cancel(); }  catch { }
        try { listener.Join(3000); } catch { }
    }
    return exitCode;
}

static void TryCreateDefaultConfig()
{
    try {
        var sb = new StringBuilder();
        sb.AppendLine("# New Era config");
        sb.AppendLine("CHAT_ID=");
        sb.AppendLine("TOKEN=");
        sb.AppendLine("API_URL=https://chat.qwen.ai");
        sb.AppendLine("MODEL=qwen3.8-max-preview");
        sb.AppendLine("QWEN_VERSION=0.2.66");
        sb.AppendLine("AI2_TOKEN=");
        sb.AppendLine("AI2_CHAT_ID=");
        sb.AppendLine("AI2_MODEL=qwen3.7-max");
        sb.AppendLine("AI2_DISPATCHER=1");
        sb.AppendLine("AI2_COMPRESS=1");
        sb.AppendLine("AI2_EXTRACT=1");
        sb.AppendLine("AI2_VALIDATE=0");
        sb.AppendLine("ARC_MODE=1");
        sb.AppendLine("PROJECT_PATH=");
        sb.AppendLine("# P1: настраиваемые лимиты");
        sb.AppendLine("MAX_CONTEXT_TOTAL=120000");
        sb.AppendLine("MAX_CONTEXT_FILE=40000");
        sb.AppendLine("MAX_HISTORY_ENTRIES=200");
        sb.AppendLine("PLAN_MAX_RETRIES=10");
        sb.AppendLine("PLAN_RETRY_DELAY_MS=3000");
        File.WriteAllText(ConfigFile, sb.ToString(), new UTF8Encoding(false));
    } catch { }
}

static void LoadAppVersion()
{
    if (File.Exists(VersionFile)) {
        string v = ReadTextAuto(VersionFile).Trim();
        if (v.Length > 0 && v.Length <= 20) AppVersion = v;
    }
}

static void StartHelperWatchdog()
{
    lock (HelperLock) {
        if (WatchdogEnabled) return;
        WatchdogEnabled = true;
        HelperWatchdogThread = new Thread(delegate() { WatchdogLoop(); });
        HelperWatchdogThread.IsBackground = true;
        HelperWatchdogThread.Start();
    }
}

static void WatchdogLoop()
{
    int failCount = 0;
    while (WatchdogEnabled && !StopRequested) {
        try {
            if (LiveRequested && !IsLiveRunning()) {
                failCount++;
                if (failCount <= 5) {
                    WriteColored(ConsoleColor.Yellow, "  \u26A0 watchdog: helper.exe не отвечает — перезапуск (" + failCount + "/5)\n");
                    RestartLiveHelper();
                } else {
                    WriteColored(ConsoleColor.Red, "  \u2716 watchdog: helper.exe не перезапускается. /live остановлен.\n");
                    LiveRequested = false;
                }
            } else {
                failCount = 0;
            }
        } catch { }
        int slept = 0;
        while (slept < 5000 && WatchdogEnabled && !StopRequested) {
            Thread.Sleep(250);
            slept += 250;
        }
    }
}

static void RestartLiveHelper()
{
    try { StopLive(); } catch { }
    try {
        if (!string.IsNullOrEmpty(LastLiveArgs))
            LaunchHelperLive(LastLiveArgs);
    } catch { }
}

static void Repl()
{
    while (!StopRequested) {
        DrawPrompt();
        string input;
        try { input = Console.ReadLine(); } catch { break; }
        if (input == null) break;

        string trimmed = input.Trim();
        if (trimmed.Length == 0) continue;

        string cmd = trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed;
        string lower = cmd.ToLowerInvariant();

        if (lower == "exit" || lower == "quit" || lower == "q") break;
        if (lower == "clear" || lower == "cls") { try { Console.Clear(); } catch { } DrawBanner(); continue; }
        if (lower == "help" || lower == "?") { DrawHelp(); continue; }
        if (lower == "status") { DrawStatus(); continue; }
        if (lower == "history" || lower == "hist") { ShowHistory(); continue; }
        if (lower == "history clear") { ClearHistory(); continue; }
        if (lower == "fetch") { LaunchHelper("--no-pause"); continue; }
        if (lower == "live") { LaunchHelperLive("--watch --no-pause"); continue; }
        if (lower == "tail") { LaunchHelperLive("--watch --tail --no-pause"); continue; }
        if (lower == "stop") { StopLive(); continue; }
        if (lower == "think on")  { ShowThinking = true;  WriteColored(ConsoleColor.Green, "  \u2714 Ход мыслей: ON\n"); continue; }
        if (lower == "think off") { ShowThinking = false; WriteColored(ConsoleColor.DarkGray, "  \u2714 Ход мыслей: OFF\n"); continue; }
        if (lower == "anim on")   { AnimationsEnabled = true;  WriteColored(ConsoleColor.Green, "  \u2714 Анимации: ON\n"); continue; }
        if (lower == "anim off")  { AnimationsEnabled = false; WriteColored(ConsoleColor.DarkGray, "  \u2714 Анимации: OFF\n"); continue; }
        if (lower == "dispatcher status") { DrawDispatcherStatus(); continue; }

        if (lower.StartsWith("test") && (lower == "test" || lower.StartsWith("test "))) { HandleTest(cmd); continue; }
        if (lower.StartsWith("scan") && (lower == "scan" || lower.StartsWith("scan "))) { HandleScan(cmd); continue; }
        if (lower.StartsWith("plan") && (lower == "plan" || lower.StartsWith("plan "))) { HandlePlan(cmd); continue; }
        if (lower.StartsWith("edit") && (lower == "edit" || lower.StartsWith("edit "))) { HandleEdit(cmd); continue; }
        if (lower.StartsWith("idea") && (lower == "idea" || lower.StartsWith("idea "))) { HandleIdea(cmd); continue; }

        string message = trimmed;
        if (lower.StartsWith("say "))  message = trimmed.Substring(trimmed.StartsWith("/") ? 5 : 4).Trim();
        else if (lower.StartsWith("send ")) message = trimmed.Substring(trimmed.StartsWith("/") ? 6 : 5).Trim();

        if (message.Length == 0) continue;
        Say(message);
    }
    WriteColored(ConsoleColor.DarkGray, "\r  \u25C2 выход.\n");
}
}
class RollbackEntry
{
public string Path;
public string Content;
public DateTime Time;
}